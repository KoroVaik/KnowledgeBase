namespace KnowledgeBase.Core.Storage;

// Server-side read of an object's bytes, for the pipeline that has to look at the content to
// analyse it. Separate from IAssetStorage, which stays browser-facing (sign, list, delete):
// the "backend does not touch bytes" rule is about the request path, not a background job.
// Decoding the bytes (text, PDF, ...) is the extractor's job, not this one's.
public interface IAssetContentReader
{
    Task<byte[]> ReadBytesAsync(string storedFileName, CancellationToken cancellationToken);
}
