using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Worker.EventClustering;

public sealed class EventCandidateHandler(
    KnowledgeBaseDbContext database,
    IOptions<EventClusteringOptions> options) : IPipelineHandler
{
    private const string ModelKey = "deterministic reviewed-event clustering";
    private readonly EventClusteringOptions _options = options.Value;

    public JobKind Kind => JobKind.AnalyzeEventCandidates;

    public bool RequiresContentAnalyzer => false;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var assets = (await database.Assets.ToListAsync(cancellationToken))
            .Where(asset => ProcessableContent.Classify(asset.ContentType, asset.OriginalFileName) is ContentKind.Image)
            .GroupBy(asset => asset.ContentSha256 ?? asset.Id)
            .Select(group => group.OrderBy(asset => asset.UploadedAtUtc).ThenBy(asset => asset.Id).First())
            .OrderBy(asset => asset.Id)
            .ToList();
        var peopleByAsset = (await (
            from reference in database.PersonReferenceFaces
            join occurrence in database.FaceOccurrences on reference.FaceOccurrenceId equals occurrence.Id
            select new { occurrence.AssetId, reference.PersonId }).ToListAsync(cancellationToken))
            .GroupBy(item => item.AssetId).ToDictionary(group => group.Key, group => group.Select(item => item.PersonId).ToHashSet());
        var locationsByAsset = (await database.LocationObservations.ToListAsync(cancellationToken))
            .GroupBy(item => item.AssetId).ToDictionary(group => group.Key, group => group.Select(item => item.LocationId).ToHashSet());
        var observationKindsByAsset = (await (
            from observation in database.SceneObservations
            join decision in database.SceneObservationReviewDecisions on observation.Id equals decision.ObservationId
            where decision.Kind == SceneObservationDecisionKind.Confirmed
            select new { observation.AssetId, observation.Kind }).ToListAsync(cancellationToken))
            .GroupBy(item => item.AssetId).ToDictionary(group => group.Key, group => group.Select(item => item.Kind).ToHashSet());
        var embeddings = await (
            from embedding in database.VisualEmbeddings
            join embeddingRun in database.PhotoAnalysisRuns on embedding.RunId equals embeddingRun.Id
            select new { embedding.AssetId, embedding.Embedding, embedding.CreatedAtUtc, embeddingRun.ModelKey, embeddingRun.ConfigurationHash }).ToListAsync(cancellationToken);
        var embeddingsByAsset = embeddings.GroupBy(item => item.AssetId)
            .ToDictionary(group => group.Key, group =>
            {
                var embedding = group.OrderByDescending(item => item.CreatedAtUtc).First();
                return new SceneEmbedding(embedding.Embedding, embedding.ModelKey, embedding.ConfigurationHash);
            });

        var photos = assets.Select(asset => new ClusterPhoto(
            asset,
            peopleByAsset.GetValueOrDefault(asset.Id) ?? [],
            locationsByAsset.GetValueOrDefault(asset.Id) ?? [],
            observationKindsByAsset.GetValueOrDefault(asset.Id) ?? [],
            embeddingsByAsset.GetValueOrDefault(asset.Id))).ToList();
        var edges = new List<ClusterEdge>();
        var parent = Enumerable.Range(0, photos.Count).ToArray();
        for (var left = 0; left < photos.Count; left++)
        for (var right = left + 1; right < photos.Count; right++)
        {
            var edge = Score(photos[left], photos[right]);
            if (edge.Score < _options.PairScoreThreshold) continue;
            edges.Add(edge with { LeftIndex = left, RightIndex = right });
            Union(parent, left, right);
        }

        var now = DateTime.UtcNow;
        var run = new EventClusteringRun
        {
            Id = Guid.NewGuid().ToString("N"), PipelineVersion = "event-clustering/v1", ModelKey = ModelKey,
            ConfigurationHash = ConfigurationHash(), CompletedAtUtc = now
        };
        database.EventClusteringRuns.Add(run);
        var unreviewedClusters = await (
            from candidate in database.EventCandidates
            where candidate.SupersededAtUtc == null
                && !database.EventCandidateReviewDecisions.Any(decision => decision.CandidateId == candidate.Id)
            select candidate.ClusterId).ToListAsync(cancellationToken);
        await database.EventCandidates
            .Where(candidate => candidate.SupersededAtUtc == null
                && !database.EventCandidateReviewDecisions.Any(decision => decision.CandidateId == candidate.Id))
            .ExecuteUpdateAsync(update => update.SetProperty(candidate => candidate.SupersededAtUtc, now), cancellationToken);
        if (unreviewedClusters.Count > 0)
            await database.EventClusters.Where(cluster => unreviewedClusters.Contains(cluster.Id) && cluster.SupersededAtUtc == null)
                .ExecuteUpdateAsync(update => update.SetProperty(cluster => cluster.SupersededAtUtc, now), cancellationToken);

        var components = edges.GroupBy(edge => Find(parent, edge.LeftIndex))
            .Select(group => new
            {
                PhotoIndexes = group.SelectMany(edge => new[] { edge.LeftIndex, edge.RightIndex }).Distinct().OrderBy(index => index).ToList(),
                Edges = group.ToList()
            });
        foreach (var component in components)
        {
            var clusterId = Guid.NewGuid().ToString("N");
            var clusterPhotos = component.PhotoIndexes.Select(index => photos[index]).ToList();
            var score = component.Edges.Average(edge => edge.Score);
            database.EventClusters.Add(new EventCluster
            {
                Id = clusterId, RunId = run.Id, Score = score, CreatedAtUtc = now,
                SignalsJson = JsonSerializer.Serialize(new
                {
                    metric = "weighted reviewed signals", threshold = _options.PairScoreThreshold,
                    edges = component.Edges.Select(edge => new
                    {
                        leftAssetId = photos[edge.LeftIndex].Asset.Id, rightAssetId = photos[edge.RightIndex].Asset.Id,
                        edge.Score, edge.TimeScore, edge.SharedPersonCount, edge.SharedLocationCount,
                        edge.VisualSimilarity, edge.SharedObservationKinds
                    })
                })
            });
            database.EventClusterPhotos.AddRange(clusterPhotos.Select(photo => new EventClusterPhoto { ClusterId = clusterId, AssetId = photo.Asset.Id }));
            database.EventCandidates.Add(new EventCandidate
            {
                Id = Guid.NewGuid().ToString("N"), ClusterId = clusterId, SuggestedOccurredOn = SuggestedDate(clusterPhotos),
                Score = score, CreatedAtUtc = now
            });
        }

        return null;
    }

    private ClusterEdge Score(ClusterPhoto left, ClusterPhoto right)
    {
        var timeScore = TimeScore(left.Asset.CapturedAtUtc, right.Asset.CapturedAtUtc);
        var sharedPersonCount = left.PersonIds.Intersect(right.PersonIds).Count();
        var sharedLocationCount = left.LocationIds.Intersect(right.LocationIds).Count();
        var sharedObservationKinds = left.ObservationKinds.Intersect(right.ObservationKinds).Select(kind => kind.ToString()).OrderBy(kind => kind).ToList();
        var visualSimilarity = VisualSimilarity(left.Embedding, right.Embedding);
        return new ClusterEdge(-1, -1,
            timeScore + (sharedPersonCount > 0 ? 0.30 : 0) + (sharedLocationCount > 0 ? 0.30 : 0)
            + (visualSimilarity is { } similarity ? Math.Max(0, similarity) * 0.20 : 0)
            + (sharedObservationKinds.Count > 0 ? 0.05 : 0),
            timeScore, sharedPersonCount, sharedLocationCount, visualSimilarity, sharedObservationKinds);
    }

    private static double TimeScore(DateTime? left, DateTime? right)
    {
        if (left is null || right is null) return 0;
        var interval = (left.Value - right.Value).Duration();
        return interval <= TimeSpan.FromHours(12) ? 0.30 : interval <= TimeSpan.FromHours(36) ? 0.15 : 0;
    }

    private static double? VisualSimilarity(SceneEmbedding? left, SceneEmbedding? right)
    {
        if (left is null || right is null || left.ModelKey != right.ModelKey || left.ConfigurationHash != right.ConfigurationHash || left.Embedding.Length != right.Embedding.Length) return null;
        double dot = 0, leftLength = 0, rightLength = 0;
        for (var index = 0; index < left.Embedding.Length; index++) { dot += left.Embedding[index] * right.Embedding[index]; leftLength += left.Embedding[index] * left.Embedding[index]; rightLength += right.Embedding[index] * right.Embedding[index]; }
        return leftLength == 0 || rightLength == 0 ? null : dot / Math.Sqrt(leftLength * rightLength);
    }

    private static DateOnly? SuggestedDate(IReadOnlyList<ClusterPhoto> photos)
    {
        var dates = photos.Where(photo => photo.Asset.CapturedAtUtc is not null).Select(photo => DateOnly.FromDateTime(photo.Asset.CapturedAtUtc!.Value)).Distinct().ToList();
        return dates.Count == 1 ? dates[0] : null;
    }

    private string ConfigurationHash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{ModelKey}|{_options.PairScoreThreshold}|v1")));
    private static int Find(int[] parent, int index) => parent[index] == index ? index : parent[index] = Find(parent, parent[index]);
    private static void Union(int[] parent, int left, int right) { left = Find(parent, left); right = Find(parent, right); if (left != right) parent[right] = left; }

    private sealed record SceneEmbedding(float[] Embedding, string ModelKey, string ConfigurationHash);
    private sealed record ClusterPhoto(AssetRecord Asset, HashSet<string> PersonIds, HashSet<string> LocationIds, HashSet<SceneObservationKind> ObservationKinds, SceneEmbedding? Embedding);
    private sealed record ClusterEdge(int LeftIndex, int RightIndex, double Score, double TimeScore, int SharedPersonCount, int SharedLocationCount, double? VisualSimilarity, IReadOnlyList<string> SharedObservationKinds);
}
