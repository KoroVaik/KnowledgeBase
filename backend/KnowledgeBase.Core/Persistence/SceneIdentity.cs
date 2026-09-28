namespace KnowledgeBase.Core.Persistence;

/// <summary>
/// Stable identity for one photo's scene across pipeline versions. Every
/// VisualEmbedding a re-analysis stores for that asset points here, so
/// review decisions (confirmed location, excluded, negative ids) attach
/// to the identity instead of to any single embedding row.
/// </summary>
public sealed class SceneIdentity
{
    public required string Id { get; init; }
    public required string AssetId { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}
