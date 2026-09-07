namespace Backend.Controllers.Assets.Contracts;

public sealed record AssetSummaryResponse(
    string StoredFileName,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc);
