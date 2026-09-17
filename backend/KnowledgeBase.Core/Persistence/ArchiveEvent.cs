namespace KnowledgeBase.Core.Persistence;

public sealed class ArchiveEvent
{
    public required string Id { get; init; }
    public required string Title { get; set; }
    public DateOnly? OccurredOn { get; set; }
    public string? LocationId { get; set; }
    public required DateTime CreatedAtUtc { get; init; }
}
