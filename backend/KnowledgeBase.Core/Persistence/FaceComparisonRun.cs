namespace KnowledgeBase.Core.Persistence;

public sealed class FaceComparisonRun
{
    public required string Id { get; init; }
    public required string AssetId { get; init; }
    public required string JobId { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public string? ContentSha256 { get; set; }
    public bool IsSkipped { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public List<FaceComparisonResult> Results { get; set; } = [];
}

public sealed class FaceComparisonResult
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required string ModelId { get; init; }
    public required string ModelName { get; init; }
    public string ConfigurationJson { get; set; } = "{}";
    public string? Error { get; set; }
    public double? ElapsedMilliseconds { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? MissedFaces { get; set; }
    public List<FaceComparisonDetection> Detections { get; set; } = [];
}

public sealed class FaceComparisonDetection
{
    public required string Id { get; init; }
    public required string ResultId { get; init; }
    public int Ordinal { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public double Score { get; init; }
    public required string LandmarksJson { get; init; }
    public required string WarningsJson { get; init; }
    public bool? IsFace { get; set; }
}
