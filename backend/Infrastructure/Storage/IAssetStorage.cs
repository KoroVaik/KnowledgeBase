namespace Backend.Infrastructure.Storage;

public sealed record StoredAsset(string Id, string FileName);

public sealed record AssetSummary(string FileName, long SizeBytes, DateTimeOffset LastModifiedUtc);

public interface IAssetStorage
{
    Task<StoredAsset> SaveAsync(Stream content, string? originalFileName, CancellationToken cancellationToken);

    Task<IReadOnlyList<AssetSummary>> ListAsync(CancellationToken cancellationToken);

    Task<Stream?> OpenReadAsync(string fileName, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string fileName, CancellationToken cancellationToken);
}
