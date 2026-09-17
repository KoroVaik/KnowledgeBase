namespace KnowledgeBase.Core.Persistence;

public sealed class LocationObservation
{
    public required string Id { get; init; }
    public required string LocationId { get; init; }
    public required string AssetId { get; init; }
    public required string VisualEmbeddingId { get; init; }
    public required string SourceDecisionId { get; init; }
    public required DateTime ConfirmedAtUtc { get; init; }
}
