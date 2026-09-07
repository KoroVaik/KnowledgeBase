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

    public Task<IReadOnlyList<AssetSummary>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AssetSummary> assets = new DirectoryInfo(_root)
            .EnumerateFiles()
            .Select(file => new AssetSummary(
                file.Name,
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)))
            .OrderByDescending(asset => asset.LastModifiedUtc)
            .ToList();

        return Task.FromResult(assets);
    }

    public Task<Stream?> OpenReadAsync(string fileName, CancellationToken cancellationToken)
    {
        var path = Resolve(fileName);

        return Task.FromResult<Stream?>(
            path is null || !File.Exists(path) ? null : File.OpenRead(path));
    }

    public Task<bool> DeleteAsync(string fileName, CancellationToken cancellationToken)
    {
        var path = Resolve(fileName);

        if (path is null || !File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);

        return Task.FromResult(true);
    }

    private string? Resolve(string fileName) =>
        AssetFileName.IsSafe(fileName) ? Path.Combine(_root, fileName) : null;
}
