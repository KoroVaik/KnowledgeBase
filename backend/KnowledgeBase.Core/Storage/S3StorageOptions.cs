namespace KnowledgeBase.Core.Storage;

public sealed class S3StorageOptions
{
    public const string SectionName = "Storage:S3";

    public string ServiceUrl { get; set; } = string.Empty;

    // SigV4 signs the region; Garage v2.3.0 accepts any value. AWS and R2 do check it.
    public string Region { get; set; } = "garage";

    public string BucketName { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    // A presigned URL is a bearer credential in a query string. Short life is the only limit -
    // the bucket cannot forget one.
    public TimeSpan LinkLifetime { get; set; } = TimeSpan.FromMinutes(5);

    // Vhost-style needs wildcard DNS a tunnel hostname cannot provide.
    public bool ForcePathStyle { get; set; } = true;
}
