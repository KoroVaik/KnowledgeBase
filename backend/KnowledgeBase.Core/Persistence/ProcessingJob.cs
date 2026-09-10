namespace KnowledgeBase.Core.Persistence;

public sealed class ProcessingJob
{
    public required string Id { get; init; }

    public required JobKind Kind { get; init; }

    // Set for BuildSourceNote (the file to process), null for aggregation jobs.
    public string? AssetId { get; init; }

    // Kind-specific input as JSON. Unused by BuildSourceNote.
    public string? Payload { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public ProcessingStatus Status { get; set; }

    public int Attempts { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? Error { get; set; }

    public static ProcessingJob Queue(string assetId) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Kind = JobKind.BuildSourceNote,
        AssetId = assetId,
        CreatedAtUtc = DateTime.UtcNow,
        Status = ProcessingStatus.Pending,
    };
}
