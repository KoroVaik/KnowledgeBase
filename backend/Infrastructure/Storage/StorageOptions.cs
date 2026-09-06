namespace Backend.Infrastructure.Storage;

public enum AssetStorageProvider
{
    Local,
    S3,
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public AssetStorageProvider Provider { get; set; } = AssetStorageProvider.Local;

    public string AssetsPath { get; set; } = Path.Combine("data", "assets");

    // Soft cap only: ASP.NET has already buffered the multipart body by the time this is
    // checked (framework default 128 MB). Needs a streaming path once real uploads land.
    public long MaxUploadBytes { get; set; } = 25L * 1024 * 1024;
}
