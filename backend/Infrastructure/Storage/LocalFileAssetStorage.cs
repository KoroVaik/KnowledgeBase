using Microsoft.Extensions.Options;

namespace Backend.Infrastructure.Storage;

public sealed class LocalFileAssetStorage : IAssetStorage
{
    private readonly string _root;

    public LocalFileAssetStorage(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        var configuredPath = options.Value.AssetsPath;

        _root = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);

        // Created eagerly so concurrent first uploads cannot race on it.
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredAsset> SaveAsync(
        Stream content,
        string? originalFileName,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var fileName = AssetFileName.For(id, originalFileName);

        await using var target = File.Create(Path.Combine(_root, fileName));
        await content.CopyToAsync(target, cancellationToken);

        return new StoredAsset(id, fileName);
    }
}
