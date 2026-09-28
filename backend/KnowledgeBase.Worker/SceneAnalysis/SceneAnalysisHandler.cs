using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.Pipeline.SceneAnalysis;
using KnowledgeBase.Core.SceneAnalysis;
using KnowledgeBase.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Worker.SceneAnalysis;

public sealed class SceneAnalysisHandler(
    KnowledgeBaseDbContext database,
    IAssetContentReader reader,
    ISceneEmbedder embedder) : IPipelineHandler
{
    public const string PipelineVersion = "scene-analysis/v2";

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

        var sceneIdentity = await database.SceneIdentities
            .SingleOrDefaultAsync(si => si.AssetId == asset.Id, cancellationToken);
        if (sceneIdentity is null)
        {
            sceneIdentity = new SceneIdentity
            {
                Id = Guid.NewGuid().ToString("N"),
                AssetId = asset.Id,
                CreatedAtUtc = now
            };
            database.SceneIdentities.Add(sceneIdentity);
        }

        var run = new PhotoAnalysisRun
        {
            Id = Guid.NewGuid().ToString("N"),
            AssetId = asset.Id,
            PipelineVersion = PipelineVersion,
            ModelKey = embedder.ModelKey,
            ConfigurationHash = embedder.ConfigurationHash,
            CompletedAtUtc = now
        };
        var visualEmbedding = new VisualEmbedding
        {
            Id = Guid.NewGuid().ToString("N"),
            RunId = run.Id,
            AssetId = asset.Id,
            SceneIdentityId = sceneIdentity.Id,
            Embedding = await embedder.EmbedAsync(await reader.ReadBytesAsync(asset.StoredFileName, cancellationToken), cancellationToken),
            CreatedAtUtc = now
        };
        database.PhotoAnalysisRuns.Add(run);
        database.VisualEmbeddings.Add(visualEmbedding);

        await ClusterScenesQueue.EnqueueAsync(database, cancellationToken);

        return null;
    }
}
