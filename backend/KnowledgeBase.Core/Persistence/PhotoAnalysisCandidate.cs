namespace KnowledgeBase.Core.Persistence;

public sealed class PhotoAnalysisCandidate
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required PhotoAnalysisCandidateKind Kind { get; init; }
    public required string SubjectAssetId { get; init; }
    public string? SubjectFaceOccurrenceId { get; init; }
    // Historical target id: deliberately no FK, so the model result survives a later merge/delete.
    public string? ProposedTargetId { get; init; }
    public string? ProposedLabel { get; init; }
    public required int Rank { get; init; }
    public required double Score { get; init; }
    public required string SignalsJson { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public DateTime? SupersededAtUtc { get; set; }
}

public enum PhotoAnalysisCandidateKind { Person, Location, Event }
