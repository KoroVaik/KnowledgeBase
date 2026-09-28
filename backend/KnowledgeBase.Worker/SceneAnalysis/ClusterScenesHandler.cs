using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.SceneAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Worker.SceneAnalysis;

// Regroups open scenes of the archive into review rows and rewrites the open review set:
// each run supersedes undecided location candidates and writes fresh candidates pointing at
// the row (SceneCluster) it landed in.
public sealed class ClusterScenesHandler(
    KnowledgeBaseDbContext database,
    IOptions<SceneClusteringOptions> options) : IPipelineHandler
{
    public const string PipelineVersion = "scene-clustering/v1";

    public JobKind Kind => JobKind.ClusterScenes;

    public bool RequiresContentAnalyzer => false;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        // Wait for pending fingerprint or scene analysis jobs to complete before clustering
        if (await database.ProcessingJobs.AnyAsync(
                other => other.Id != job.Id
                    && (other.Kind == JobKind.FingerprintAsset || other.Kind == JobKind.AnalyzeScenes)
                    && (other.Status == ProcessingStatus.Pending || other.Status == ProcessingStatus.Running),
                cancellationToken))
            return null;

        var thresholds = options.Value;
        var now = DateTime.UtcNow;
        var states = await SceneIdentityReviewStates.LoadAsync(database, cancellationToken);
        var locations = await database.Locations.ToDictionaryAsync(loc => loc.Id, loc => loc.Name, cancellationToken);

        var references = await (
            from obs in database.LocationObservations
            join emb in database.VisualEmbeddings on obs.VisualEmbeddingId equals emb.Id
            join loc in database.Locations on obs.LocationId equals loc.Id
            select new LocationReferenceEmbedding(loc.Id, loc.Name, emb.Id, emb.Embedding)
        ).ToListAsync(cancellationToken);

        var canonicalAssetIds = (await database.Assets.ToListAsync(cancellationToken))
            .GroupBy(asset => asset.ContentSha256 ?? asset.Id)
            .Select(group => group.OrderBy(asset => asset.UploadedAtUtc).ThenBy(asset => asset.Id).First().Id)
            .ToHashSet();

        var openScenesQuery = await (
            from si in database.SceneIdentities
            join emb in database.VisualEmbeddings on si.Id equals emb.SceneIdentityId
            join asset in database.Assets on si.AssetId equals asset.Id
            join run in database.PhotoAnalysisRuns on emb.RunId equals run.Id
            where canonicalAssetIds.Contains(si.AssetId) && !states.SettledIds.Contains(si.Id)
            orderby emb.CreatedAtUtc descending
            select new
            {
                si.Id,
                si.AssetId,
                emb.Embedding,
                emb.RunId,
                run.ModelKey,
                asset.Latitude,
                asset.Longitude
            }
        ).ToListAsync(cancellationToken);

        // Deduplicate: latest embedding per SceneIdentity
        var openScenes = openScenesQuery
            .GroupBy(s => s.Id)
            .Select(g => g.First())
            .ToList();

        var embeddingLength = openScenes.Select(s => s.Embedding.Length)
            .Concat(references.Select(r => r.Embedding.Length))
            .GroupBy(len => len)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        openScenes = openScenes.Where(s => s.Embedding.Length == embeddingLength).ToList();
        references = references.Where(r => r.Embedding.Length == embeddingLength).ToList();

        var scenesInput = openScenes
            .Select(s => new SceneClusteringScene(s.Id, s.Embedding, s.Latitude, s.Longitude))
            .ToList();

        var result = SceneClustering.Run(scenesInput, references, states, thresholds);

        var configurationHash = ConfigurationHash(thresholds);
        var clusteringRun = new SceneClusteringRun
        {
            Id = Guid.NewGuid().ToString("N"),
            PipelineVersion = PipelineVersion,
            ConfigurationHash = configurationHash,
            CompletedAtUtc = now
        };
        database.SceneClusteringRuns.Add(clusteringRun);

        var stale = await database.PhotoAnalysisCandidates
            .Where(candidate => candidate.Kind == PhotoAnalysisCandidateKind.Location
                && candidate.SupersededAtUtc == null
                && !database.PhotoAnalysisReviewDecisions.Any(decision => decision.CandidateId == candidate.Id))
            .ToListAsync(cancellationToken);
        foreach (var candidate in stale) candidate.SupersededAtUtc = now;

        var placements = new List<Placement>();
        SceneCluster AddCluster(SceneClusterKind kind, string? locationId = null, string? excludedGroupId = null, string? hintLocationId = null, double? hintScore = null)
        {
            var cluster = new SceneCluster
            {
                Id = Guid.NewGuid().ToString("N"),
                RunId = clusteringRun.Id,
                Kind = kind,
                LocationId = locationId,
                ExcludedGroupId = excludedGroupId,
                HintLocationId = hintLocationId,
                HintScore = hintScore,
                CreatedAtUtc = now
            };
            database.SceneClusters.Add(cluster);
            return cluster;
        }

        foreach (var locationGroup in result.LocationJoins.GroupBy(join => join.LocationId))
        {
            var cluster = AddCluster(SceneClusterKind.Location, locationId: locationGroup.Key);
            placements.AddRange(locationGroup.Select(join => new Placement(join.IdentityId, cluster, locationGroup.Count(), join.LocationId, join.BestReferenceEmbeddingId)));
        }
        foreach (var group in result.AnonymousClusters)
        {
            var cluster = AddCluster(SceneClusterKind.Anonymous, hintLocationId: group.HintLocationId, hintScore: group.HintScore);
            placements.AddRange(group.IdentityIds.Select(identityId => new Placement(identityId, cluster, group.IdentityIds.Count, null, null)));
        }
        foreach (var excluded in result.ExcludedGroups)
        {
            var cluster = AddCluster(SceneClusterKind.Excluded, excludedGroupId: excluded.GroupId, hintLocationId: excluded.HintLocationId, hintScore: excluded.HintScore);
            placements.AddRange(excluded.IdentityIds.Select(identityId => new Placement(identityId, cluster, excluded.IdentityIds.Count, null, null)));
        }
        if (result.UnsortedIdentityIds.Count > 0)
        {
            var cluster = AddCluster(SceneClusterKind.Unsorted);
            placements.AddRange(result.UnsortedIdentityIds.Select(identityId => new Placement(identityId, cluster, 1, null, null)));
        }

        var sceneByIdentity = openScenes.ToDictionary(s => s.Id);
        var runIdByAsset = new Dictionary<string, string>();
        foreach (var rowGroup in placements.GroupBy(placement => placement.Cluster.Id))
        {
            var rank = 0;
            foreach (var placement in rowGroup.OrderByDescending(placement => result.OrderScores.GetValueOrDefault(placement.IdentityId)))
            {
                rank++;
                var scene = sceneByIdentity[placement.IdentityId];
                if (!runIdByAsset.TryGetValue(scene.AssetId, out var runId))
                {
                    runId = Guid.NewGuid().ToString("N");
                    runIdByAsset[scene.AssetId] = runId;
                    database.PhotoAnalysisRuns.Add(new PhotoAnalysisRun
                    {
                        Id = runId,
                        AssetId = scene.AssetId,
                        PipelineVersion = PipelineVersion,
                        ModelKey = scene.ModelKey,
                        ConfigurationHash = configurationHash,
                        CompletedAtUtc = now
                    });
                }

                var cluster = placement.Cluster;
                database.PhotoAnalysisCandidates.Add(new PhotoAnalysisCandidate
                {
                    Id = Guid.NewGuid().ToString("N"),
                    RunId = runId,
                    Kind = PhotoAnalysisCandidateKind.Location,
                    SubjectAssetId = scene.AssetId,
                    SubjectFaceOccurrenceId = null,
                    ProposedTargetId = placement.LocationId,
                    ProposedLabel = placement.LocationId is not null
                        ? locations.GetValueOrDefault(placement.LocationId, "Unknown location")
                        : $"{cluster.Kind} scene",
                    Rank = rank,
                    Score = result.OrderScores.GetValueOrDefault(placement.IdentityId),
                    SignalsJson = JsonSerializer.Serialize(new
                    {
                        metric = "cosine",
                        embedder = scene.ModelKey,
                        clustering = PipelineVersion,
                        thresholds = new
                        {
                            locationJoin = thresholds.LocationJoinThreshold,
                            cluster = thresholds.ClusterThreshold,
                            hint = thresholds.HintThreshold
                        },
                        clusterKind = cluster.Kind.ToString(),
                        clusterSize = placement.ClusterSize,
                        hint = cluster.HintLocationId is null ? null : new { locationId = cluster.HintLocationId, score = cluster.HintScore },
                        referenceEmbeddingId = placement.ReferenceEmbeddingId,
                        gps = scene.Latitude.HasValue && scene.Longitude.HasValue ? new { lat = scene.Latitude.Value, lon = scene.Longitude.Value } : null
                    }),
                    CreatedAtUtc = now,
                    SceneClusterId = cluster.Id
                });
            }
        }

        Console.WriteLine($"[scene-clustering] {openScenes.Count} open scene(s): {result.LocationJoins.Count} joined locations, {result.AnonymousClusters.Count} anonymous group(s), {result.ExcludedGroups.Count} excluded group(s), {result.UnsortedIdentityIds.Count} unsorted.");
        return null;
    }

    private static string ConfigurationHash(SceneClusteringOptions thresholds) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant(
            $"{PipelineVersion}|{thresholds.LocationJoinThreshold}|{thresholds.ClusterThreshold}|{thresholds.HintThreshold}"))));

    private sealed record Placement(
        string IdentityId,
        SceneCluster Cluster,
        int ClusterSize,
        string? LocationId,
        string? ReferenceEmbeddingId);
}
