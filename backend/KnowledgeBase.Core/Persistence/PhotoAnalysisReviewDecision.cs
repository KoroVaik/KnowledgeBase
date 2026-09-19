namespace KnowledgeBase.Core.Persistence;

public sealed class PhotoAnalysisReviewDecision
{
    public required string Id { get; init; }
    public required string CandidateId { get; init; }
    public required PhotoAnalysisDecisionKind Kind { get; init; }
    public string? ChosenTargetId { get; init; }
    public string? Note { get; init; }
    public required DateTime DecidedAtUtc { get; init; }
}

public enum PhotoAnalysisDecisionKind { Accepted, Rejected, Corrected, Merged, Ignored }
