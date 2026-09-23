namespace KnowledgeBase.Core.Persistence;

public sealed class FaceOccurrence
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required string AssetId { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required double DetectionScore { get; init; }
    // A real face that touches a photo edge. It remains reviewable, but should not become a
    // reference face or influence automatic person matching until a later policy permits it.
    public required bool IsPartial { get; init; }
    public required string LandmarksJson { get; init; }
    // Re-embeddable: an embedder swap rewrites both fields in place instead of re-detecting.
    public required float[] Embedding { get; set; }
    public string? EmbeddingModelKey { get; set; }
    // Assigned when the occurrence is stored (or by the migration pass for legacy rows), so the
    // column is null only between a schema upgrade and the worker's first identity assignment.
    public string? IdentityId { get; set; }
    public required DateTime CreatedAtUtc { get; init; }
}
