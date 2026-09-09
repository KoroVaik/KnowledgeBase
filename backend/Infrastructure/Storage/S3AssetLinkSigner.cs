using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Backend.Infrastructure.Storage;

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

        // The SDK signs https by default and ignores the scheme of ServiceUrl, so a local
        // Garage on plain http would be handed links to a port that speaks no TLS.
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

            // The object sits under a GUID, and objects stored before direct uploads carry no
            // useful type at all. These overrides are what still makes the browser save
            // "my report.pdf" - the metadata lives in the table, so the link has to carry it.
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

            // Part of the signature, so the browser must send exactly this header - and the
            // object lands in the bucket already carrying the type, which SaveAsync never set.
            ContentType = effectiveContentType,
        });

        return new SignedUpload(fileName, url, effectiveContentType, expiresAt);
    }

    // RFC 6266: the quoted filename is what old clients read, filename* carries everything
    // outside ASCII. Sending only the first would mangle a Cyrillic name, only the second
    // would leave some clients with nothing.
    private static string AttachmentFor(string originalFileName)
    {
        var ascii = new string(originalFileName
            .Where(character => character is > (char)31 and < (char)127 && character != '"' && character != '\\')
            .ToArray());

        var fallback = ascii.Length == 0 ? "download" : ascii;

        return $"attachment; filename=\"{fallback}\"; filename*=UTF-8''{Uri.EscapeDataString(originalFileName)}";
    }
}
