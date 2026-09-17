namespace KnowledgeBase.Core.Persistence;

public sealed class PersonReferenceFace
{
    public required string PersonId { get; init; }
    public required string FaceOccurrenceId { get; init; }
    public required string SourceDecisionId { get; init; }
    public required DateTime ConfirmedAtUtc { get; init; }
}
