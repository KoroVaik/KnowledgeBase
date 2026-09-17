namespace KnowledgeBase.Core.Persistence;

public sealed class EventCandidate
{
    public required string Id { get; init; }
    public required string ClusterId { get; init; }
    public DateOnly? SuggestedOccurredOn { get; init; }
    public required double Score { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public DateTime? SupersededAtUtc { get; set; }
}
