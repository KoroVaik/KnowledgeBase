namespace KnowledgeBase.Core.Storage;

public sealed record StoredAsset(string Id, string FileName);

public sealed record AssetSummary(string FileName, long SizeBytes, DateTimeOffset LastModifiedUtc);

public interface IAssetStorage
{
    // Kept for the orphan sweep (objects in the bucket with no row); nothing calls it yet.
    Task<IReadOnlyList<AssetSummary>> ListAsync(CancellationToken cancellationToken);

    Task<AssetSummary?> GetAsync(string fileName, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string fileName, CancellationToken cancellationToken);
}
