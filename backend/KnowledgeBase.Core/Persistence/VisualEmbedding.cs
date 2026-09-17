namespace KnowledgeBase.Core.Persistence;

public sealed class VisualEmbedding
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required string AssetId { get; init; }
    public required float[] Embedding { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}
