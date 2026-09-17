using System.Security.Cryptography;
using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Worker.FaceAnalysis;

public sealed class AssetFingerprintHandler(KnowledgeBaseDbContext database, IAssetContentReader reader) : IPipelineHandler
{
    public JobKind Kind => JobKind.FingerprintAsset;

    public bool RequiresContentAnalyzer => false;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var assetId = job.AssetId ?? throw new InvalidOperationException($"Job {job.Id} is a fingerprint job with no asset.");
        var asset = await database.Assets.SingleOrDefaultAsync(item => item.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Asset {assetId} no longer exists.");
        if (ProcessableContent.Classify(asset.ContentType, asset.OriginalFileName) is not ContentKind.Image)
            throw new SkippableContentException("Fingerprinting for photo analysis only applies to image assets.");

        asset.ContentSha256 ??= Convert.ToHexString(SHA256.HashData(await reader.ReadBytesAsync(asset.StoredFileName, cancellationToken)));
        var canonicalAssetId = await database.Assets
            .Where(item => item.ContentSha256 == asset.ContentSha256)
            .OrderBy(item => item.UploadedAtUtc).ThenBy(item => item.Id)
            .Select(item => item.Id)
            .FirstAsync(cancellationToken);
        if (canonicalAssetId == asset.Id)
        {
            await ProcessingQueue.EnsureQueuedAsync(database, asset.Id, JobKind.AnalyzeFaces, cancellationToken);
            await ProcessingQueue.EnsureQueuedAsync(database, asset.Id, JobKind.AnalyzeScenes, cancellationToken);
        }

        return null;
    }
}
