namespace KnowledgeBase.Api.Controllers.Assets.Contracts;

public sealed record AssetSummaryResponse(
    string StoredFileName,
    string OriginalFileName,
    // The stored MIME type, so the UI can tell an image (which it can preview inline) from the rest.
    string ContentType,
    long SizeBytes,
    DateTime UploadedAtUtc,
    // Null when the type is not one the pipeline picks up (no job was queued). Otherwise the
    // ProcessingJob status: Pending, Running, Done, Failed or Skipped.
    string? ProcessingStatus,
    // The job's error text, for Failed and Skipped. Null otherwise.
    string? ProcessingError,
    // The note the pipeline produced from this file, once it has. Null until then.
    string? NoteId);
