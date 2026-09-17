namespace KnowledgeBase.Core.Persistence;

public sealed class SceneObservation
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required string AssetId { get; init; }
    public required SceneObservationKind Kind { get; init; }
    public string? SubjectPersonId { get; init; }
    public string? SubjectPersonName { get; init; }
    public string? RelatedPersonId { get; init; }
    public string? RelatedPersonName { get; init; }
    public required string Description { get; init; }
    public required string Evidence { get; init; }
    public required double Confidence { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public DateTime? SupersededAtUtc { get; set; }
}

public enum SceneObservationKind { Action, Interaction, Object, Text, Mood }
