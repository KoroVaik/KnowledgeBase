namespace Backend.Controllers.Assets.Contracts;

public sealed record UploadedAssetResponse(
    string Id,
    string StoredFileName,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    DateTime UploadedAtUtc);
