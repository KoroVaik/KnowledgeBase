namespace KnowledgeBase.Core.Storage;

// Server-side read of an object's bytes, for the pipeline that has to look at the content to
// analyse it. Separate from IAssetStorage, which stays browser-facing (sign, list, delete):
// the "backend does not touch bytes" rule is about the request path, not a background job.
public interface IAssetContentReader
{
    Task<string> ReadTextAsync(string storedFileName, CancellationToken cancellationToken);

    Task<byte[]> ReadBytesAsync(string storedFileName, CancellationToken cancellationToken);
}
