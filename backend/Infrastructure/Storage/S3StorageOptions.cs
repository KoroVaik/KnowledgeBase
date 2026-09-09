namespace Backend.Infrastructure.Storage;

public sealed class S3StorageOptions
{
    public const string SectionName = "Storage:S3";

    public string ServiceUrl { get; set; } = string.Empty;

    // SigV4 signs the region, but Garage v2.3.0 accepts any value — verified. Kept
    // configurable for providers that do check it (AWS, R2).
    public string Region { get; set; } = "garage";

    public string BucketName { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    // A presigned URL is a bearer credential in a query string: whoever sees it (browser
    // history, proxy logs) downloads without a session. Short life is the only thing limiting
    // that, since the bucket cannot be told to forget one link.
    public TimeSpan LinkLifetime { get; set; } = TimeSpan.FromMinutes(5);

    // Vhost-style (bucket.host/key) needs wildcard DNS, which a single tunnel hostname
    // cannot provide, so path-style is the default rather than an escape hatch.
    public bool ForcePathStyle { get; set; } = true;
}
