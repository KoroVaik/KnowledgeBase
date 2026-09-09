namespace Backend.Infrastructure.Storage;

public sealed record SignedLink(string Url, DateTimeOffset ExpiresAtUtc);

public sealed record SignedUpload(string FileName, string Url, string ContentType, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Hands out URLs the browser uses to talk to the bucket without going through this API.
/// </summary>
/// <remarks>
/// Deliberately not part of <see cref="IAssetStorage"/>: only a bucket can sign, so the local
/// provider would have to implement this by throwing. An interface that is simply absent from
/// the container is a truer statement than one whose implementation refuses to work.
/// </remarks>
public interface IAssetLinkSigner
{
    SignedLink SignDownload(string fileName, string originalFileName, string contentType);

    SignedUpload SignUpload(string? originalFileName, string? contentType);
}
