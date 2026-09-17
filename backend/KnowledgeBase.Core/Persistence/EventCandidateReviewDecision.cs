namespace KnowledgeBase.Core.Persistence;

public sealed class EventCandidateReviewDecision
{
    public required string Id { get; init; }
    public required string CandidateId { get; init; }
    public required EventCandidateDecisionKind Kind { get; init; }
    public string? ChosenEventId { get; init; }
    public required string SelectedAssetIdsJson { get; init; }
    public string? Note { get; init; }
    public required DateTime DecidedAtUtc { get; init; }
}

public enum EventCandidateDecisionKind { Created, Attached, Rejected }
