using System.Text.Json;
using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.SceneAnalysis;
using KnowledgeBase.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Worker.SceneAnalysis;

public sealed class SceneAnalysisHandler(
    KnowledgeBaseDbContext database,
    IAssetContentReader reader,
    ISceneEmbedder embedder,
    IOptions<VisualAnalysisOptions> options) : IPipelineHandler
{
    private readonly VisualAnalysisOptions _options = options.Value;

    public JobKind Kind => JobKind.AnalyzeScenes;

    public bool RequiresContentAnalyzer => false;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var assetId = job.AssetId ?? throw new InvalidOperationException($"Job {job.Id} is a scene-analysis job with no asset.");
        var asset = await database.Assets.SingleOrDefaultAsync(item => item.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Asset {assetId} no longer exists.");
        if (ProcessableContent.Classify(asset.ContentType, asset.OriginalFileName) is not ContentKind.Image)
            throw new SkippableContentException("Scene analysis only applies to image assets.");
        if (asset.ContentSha256 is not null)
        {
            var canonicalAssetId = await database.Assets
                .Where(item => item.ContentSha256 == asset.ContentSha256)
                .OrderBy(item => item.UploadedAtUtc).ThenBy(item => item.Id)
                .Select(item => item.Id)
                .FirstAsync(cancellationToken);
            if (canonicalAssetId != asset.Id)
                throw new SkippableContentException("This image is an exact duplicate of an earlier archive asset.");
        }

        var now = DateTime.UtcNow;
        var run = new PhotoAnalysisRun
        {
            Id = Guid.NewGuid().ToString("N"), AssetId = asset.Id, PipelineVersion = "scene-analysis/v1",
            ModelKey = embedder.ModelKey, ConfigurationHash = embedder.ConfigurationHash, CompletedAtUtc = now
        };
        var visualEmbedding = new VisualEmbedding
        {
            Id = Guid.NewGuid().ToString("N"), RunId = run.Id, AssetId = asset.Id,
            Embedding = await embedder.EmbedAsync(await reader.ReadBytesAsync(asset.StoredFileName, cancellationToken), cancellationToken),
            CreatedAtUtc = now
        };
        database.PhotoAnalysisRuns.Add(run);
        database.VisualEmbeddings.Add(visualEmbedding);
        await database.PhotoAnalysisCandidates
            .Where(candidate => candidate.Kind == PhotoAnalysisCandidateKind.Location
                && candidate.SubjectAssetId == asset.Id
                && candidate.SupersededAtUtc == null
                && !database.PhotoAnalysisReviewDecisions.Any(decision => decision.CandidateId == candidate.Id))
            .ExecuteUpdateAsync(update => update.SetProperty(candidate => candidate.SupersededAtUtc, now), cancellationToken);

        var references = await (
            from observation in database.LocationObservations
            join referenceEmbedding in database.VisualEmbeddings on observation.VisualEmbeddingId equals referenceEmbedding.Id
            join referenceRun in database.PhotoAnalysisRuns on referenceEmbedding.RunId equals referenceRun.Id
            join location in database.Locations on observation.LocationId equals location.Id
            where referenceRun.ModelKey == embedder.ModelKey && referenceRun.ConfigurationHash == embedder.ConfigurationHash
            select new
            {
                LocationId = location.Id,
                location.Name,
                ReferenceEmbeddingId = referenceEmbedding.Id,
                ReferenceAssetId = observation.AssetId,
                referenceEmbedding.Embedding
            }).ToListAsync(cancellationToken);

        var ranked = references
            .GroupBy(reference => new { reference.LocationId, reference.Name })
            .Select(group => new
            {
                group.Key.LocationId,
                group.Key.Name,
                Best = group.OrderByDescending(reference => CosineSimilarity(visualEmbedding.Embedding, reference.Embedding)).First(),
                ReferenceCount = group.Count()
            })
            .Select(item => new
            {
                item.LocationId,
                item.Name,
                Score = CosineSimilarity(visualEmbedding.Embedding, item.Best.Embedding),
                item.Best.ReferenceEmbeddingId,
                item.Best.ReferenceAssetId,
                item.ReferenceCount
            })
            .OrderByDescending(item => item.Score)
            .Take(Math.Clamp(_options.CandidateCount, 1, 5))
            .ToList();

        if (ranked.Count == 0)
        {
            database.PhotoAnalysisCandidates.Add(CreateCandidate(
                visualEmbedding, 1, 0, null, "Unknown location — create a location, then correct this candidate.",
                JsonSerializer.Serialize(new { metric = "cosine", referenceCount = 0, embeddingDimension = visualEmbedding.Embedding.Length, model = embedder.ModelKey })));
            return null;
        }

        for (var index = 0; index < ranked.Count; index++)
        {
            var candidate = ranked[index];
            database.PhotoAnalysisCandidates.Add(CreateCandidate(
                visualEmbedding, index + 1, candidate.Score, candidate.LocationId, candidate.Name,
                JsonSerializer.Serialize(new
                {
                    metric = "cosine", referenceEmbeddingId = candidate.ReferenceEmbeddingId, referenceAssetId = candidate.ReferenceAssetId,
                    referenceCount = candidate.ReferenceCount, embeddingDimension = visualEmbedding.Embedding.Length, model = embedder.ModelKey
                })));
        }

        return null;
    }

    private static PhotoAnalysisCandidate CreateCandidate(VisualEmbedding embedding, int rank, double score, string? locationId, string label, string signalsJson) => new()
    {
        Id = Guid.NewGuid().ToString("N"), RunId = embedding.RunId, Kind = PhotoAnalysisCandidateKind.Location,
        SubjectAssetId = embedding.AssetId, ProposedTargetId = locationId, ProposedLabel = label,
        Rank = rank, Score = score, SignalsJson = signalsJson, CreatedAtUtc = embedding.CreatedAtUtc
    };

    private static double CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length != right.Length) throw new InvalidOperationException("Scene embeddings from different models cannot be compared.");
        double dot = 0, leftLength = 0, rightLength = 0;
        for (var index = 0; index < left.Length; index++) { dot += left[index] * right[index]; leftLength += left[index] * left[index]; rightLength += right[index] * right[index]; }
        return leftLength == 0 || rightLength == 0 ? 0 : dot / Math.Sqrt(leftLength * rightLength);
    }
}
