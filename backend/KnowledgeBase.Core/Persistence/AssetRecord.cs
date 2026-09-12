using KnowledgeBase.Core.Storage;

namespace KnowledgeBase.Core.Persistence;

public sealed class AssetRecord
{
    public required string Id { get; init; }

    public required string StoredFileName { get; init; }

    public required string OriginalFileName { get; init; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }

    public required DateTime UploadedAtUtc { get; init; }

    // EXIF, best-effort - null for a screenshot, a PNG, or a photo a messenger recompressed
    // and stripped. Filled in by the pipeline after upload, not at upload time (Confirm never
    // reads the bytes).
    public DateTime? CapturedAtUtc { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    // Takes the pieces rather than an IFormFile: the AI pipeline will be storing files nobody
    // uploaded, and an entity has no business knowing about HTTP.
    public static AssetRecord For(
        StoredAsset stored,
        string? originalFileName,
        string? contentType,
        long sizeBytes) => new()
    {
        Id = stored.Id,
        StoredFileName = stored.FileName,

        // The stored name is a poor substitute, but a nameless row would leave the UI with
        // nothing to show and the download with nothing to save under.
        OriginalFileName = string.IsNullOrWhiteSpace(originalFileName)
            ? stored.FileName
            : Path.GetFileName(originalFileName),

        ContentType = string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType,

        SizeBytes = sizeBytes,
        UploadedAtUtc = DateTime.UtcNow,
    };
}
