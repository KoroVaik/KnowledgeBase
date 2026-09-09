namespace Backend.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    // A signed PUT cannot be capped, so this is only an early "no" on the claimed size in
    // upload-link; the real check is HeadObject in confirm, once the bytes are in the bucket.
    public long MaxUploadBytes { get; set; } = 25L * 1024 * 1024;
}
