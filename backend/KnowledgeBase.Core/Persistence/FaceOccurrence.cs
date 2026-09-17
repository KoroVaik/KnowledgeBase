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
    public required string LandmarksJson { get; init; }
    public required float[] Embedding { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}
