namespace Backend.Controllers.Assets.Contracts;

public sealed record AssetSummaryResponse(
    string StoredFileName,
    string OriginalFileName,
    long SizeBytes,
    DateTime UploadedAtUtc);
