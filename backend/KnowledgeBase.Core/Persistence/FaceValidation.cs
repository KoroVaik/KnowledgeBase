namespace KnowledgeBase.Core.Persistence;

public enum FaceValidationSubject { HumanFace, AnimalFace, StatueOrArtwork, NotFace, Uncertain }
public enum FaceValidationDecisionKind { Approved, Excluded }

public sealed class FaceValidation
{
    public required string FaceOccurrenceId { get; init; }
    public required string PipelineVersion { get; set; }
    public required string ConfigurationHash { get; set; }
    public required string InputHash { get; set; }
    public required string ModelKey { get; set; }
    public int? MinSidePixels { get; set; }
    public double? Sharpness112 { get; set; }
    public bool TouchesImageEdge { get; set; }
    public FaceValidationSubject? Subject { get; set; }
    public string? Evidence { get; set; }
    public string? LastError { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public sealed class FaceValidationReviewDecision
{
    public required string Id { get; init; }
    public required string FaceIdentityId { get; init; }
    public required string FaceOccurrenceId { get; init; }
    public required FaceValidationDecisionKind Kind { get; init; }
    public required DateTime DecidedAtUtc { get; init; }
}
