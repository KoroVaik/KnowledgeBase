using System.Diagnostics;
using KnowledgeBase.Core.Storage;

namespace KnowledgeBase.Core.Observability;

internal static class StorageLogging
{
    public static async Task<T> Run<T>(ILogger logger, string operation, string? key, Func<Task<T>> action)
    {
        using var activity = OperationContext.StartActivity("storage." + operation);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await action();
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            logger.Log(elapsed >= 1000 ? LogLevel.Warning : LogLevel.Debug,
                "Storage {Operation} {AssetId} completed in {DurationMs} ms", operation, key is null ? null : AssetFileName.IdOf(key), elapsed);
            return result;
        }
        catch (Exception error)
        {
            logger.Log(error is OperationCanceledException ? LogLevel.Information : LogLevel.Error, error,
                "Storage {Operation} failed in {DurationMs} ms", operation, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}

public sealed class LoggedAssetStorage(S3AssetStorage inner, ILogger<LoggedAssetStorage> logger) : IAssetStorage
{
    public Task<IReadOnlyList<AssetSummary>> ListAsync(CancellationToken cancellationToken) =>
        StorageLogging.Run(logger, "list", null, () => inner.ListAsync(cancellationToken));
    public Task<AssetSummary?> GetAsync(string fileName, CancellationToken cancellationToken) =>
        StorageLogging.Run(logger, "head", fileName, () => inner.GetAsync(fileName, cancellationToken));
    public Task<bool> DeleteAsync(string fileName, CancellationToken cancellationToken) =>
        StorageLogging.Run(logger, "delete", fileName, () => inner.DeleteAsync(fileName, cancellationToken));
}

public sealed class LoggedAssetContentReader(S3AssetContentReader inner, ILogger<LoggedAssetContentReader> logger) : IAssetContentReader
{
    public Task<byte[]> ReadBytesAsync(string storedFileName, CancellationToken cancellationToken) =>
        StorageLogging.Run(logger, "read", storedFileName, () => inner.ReadBytesAsync(storedFileName, cancellationToken));
}
