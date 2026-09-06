namespace Backend.Infrastructure.Storage;

public sealed record StoredAsset(string Id, string FileName);

public interface IAssetStorage
{
    Task<StoredAsset> SaveAsync(Stream content, string? originalFileName, CancellationToken cancellationToken);
}
