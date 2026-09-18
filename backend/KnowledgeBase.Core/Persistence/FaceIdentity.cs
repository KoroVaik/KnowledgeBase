namespace KnowledgeBase.Core.Persistence;

// One physical face on one photo, stable across pipeline versions: every occurrence a
// re-detection stores for that face points here, so settled review states (references,
// rejections) attach to the identity instead of to any single occurrence row.
public sealed class FaceIdentity
{
    public required string Id { get; init; }
    public required string AssetId { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}
