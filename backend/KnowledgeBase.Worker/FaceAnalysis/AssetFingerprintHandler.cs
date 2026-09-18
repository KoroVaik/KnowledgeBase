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
        // The fresh hash is only tracked, not saved yet, so this query can't see this asset's own row.
        var earlierCopy = await database.Assets
            .Where(item => item.Id != asset.Id && item.ContentSha256 == asset.ContentSha256)
            .OrderBy(item => item.UploadedAtUtc).ThenBy(item => item.Id)
            .Select(item => new { item.Id, item.UploadedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);
        var isCanonical = earlierCopy is null
            || earlierCopy.UploadedAtUtc > asset.UploadedAtUtc
            || (earlierCopy.UploadedAtUtc == asset.UploadedAtUtc && earlierCopy.Id.CompareTo(asset.Id) > 0);
        if (isCanonical)
        {
            await ProcessingQueue.EnsureQueuedAsync(database, asset.Id, JobKind.AnalyzeFaces, cancellationToken);
            await ProcessingQueue.EnsureQueuedAsync(database, asset.Id, JobKind.AnalyzeScenes, cancellationToken);
        }

        return null;
    }
}
