namespace KnowledgeBase.Core.Persistence;

public sealed class SceneObservationReviewDecision
{
    public required string Id { get; init; }
    public required string ObservationId { get; init; }
    public required SceneObservationDecisionKind Kind { get; init; }
    public string? Note { get; init; }
    public required DateTime DecidedAtUtc { get; init; }
}

public enum SceneObservationDecisionKind { Confirmed, Rejected }
