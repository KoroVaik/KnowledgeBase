namespace KnowledgeBase.Core.Storage;

public sealed record SignedLink(string Url, DateTimeOffset ExpiresAtUtc);

public sealed record SignedUpload(string FileName, string Url, string ContentType, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// URLs the browser uses to talk to the bucket directly. Separate from <see cref="IAssetStorage"/>:
/// only a bucket can sign.
/// </summary>
public interface IAssetLinkSigner
{
    SignedLink SignDownload(string fileName, string originalFileName, string contentType);

    SignedUpload SignUpload(string? originalFileName, string? contentType);
}
