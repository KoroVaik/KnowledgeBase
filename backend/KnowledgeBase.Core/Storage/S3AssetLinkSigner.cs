using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Core.Storage;

public sealed class S3AssetLinkSigner : IAssetLinkSigner
{
    private const string DefaultContentType = "application/octet-stream";

    private readonly IAmazonS3 _client;
    private readonly S3StorageOptions _options;
    private readonly Protocol _protocol;

    public S3AssetLinkSigner(IAmazonS3 client, IOptions<S3StorageOptions> options)
    {
        _client = client;
        _options = options.Value;

        // The SDK signs https and ignores the ServiceUrl scheme; a local Garage on http needs HTTP.
        _protocol = Uri.TryCreate(_options.ServiceUrl, UriKind.Absolute, out var serviceUrl)
            && serviceUrl.Scheme == Uri.UriSchemeHttp
                ? Protocol.HTTP
                : Protocol.HTTPS;
    }

    public SignedLink SignDownload(string fileName, string originalFileName, string contentType)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(_options.LinkLifetime);

        var url = _client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = fileName,
            Verb = HttpVerb.GET,
            Expires = expiresAt.UtcDateTime,
            Protocol = _protocol,

            // The object is stored under a GUID; these overrides make the browser save the real name.
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = AttachmentFor(originalFileName),
                ContentType = string.IsNullOrWhiteSpace(contentType) ? DefaultContentType : contentType,
            },
        });

        return new SignedLink(url, expiresAt);
    }

    public SignedUpload SignUpload(string? originalFileName, string? contentType)
    {
        var fileName = AssetFileName.For(Guid.NewGuid().ToString("N"), originalFileName);
        var effectiveContentType = string.IsNullOrWhiteSpace(contentType) ? DefaultContentType : contentType;
        var expiresAt = DateTimeOffset.UtcNow.Add(_options.LinkLifetime);

        var url = _client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = fileName,
            Verb = HttpVerb.PUT,
            Expires = expiresAt.UtcDateTime,
            Protocol = _protocol,

            // Part of the signature, so the browser must send exactly this.
            ContentType = effectiveContentType,
        });

        return new SignedUpload(fileName, url, effectiveContentType, expiresAt);
    }

    // RFC 6266: quoted filename for old clients, filename* for non-ASCII names.
    private static string AttachmentFor(string originalFileName)
    {
        var ascii = new string(originalFileName
            .Where(character => character is > (char)31 and < (char)127 && character != '"' && character != '\\')
            .ToArray());

        var fallback = ascii.Length == 0 ? "download" : ascii;

        return $"attachment; filename=\"{fallback}\"; filename*=UTF-8''{Uri.EscapeDataString(originalFileName)}";
    }
}
