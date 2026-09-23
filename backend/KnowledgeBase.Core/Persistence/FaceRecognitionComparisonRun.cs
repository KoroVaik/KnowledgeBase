namespace KnowledgeBase.Core.Persistence;

public sealed class FaceRecognitionComparisonRun
{
    public required string Id { get; init; }
    public required string JobId { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public int PhotoCount { get; set; }
    public int ReferenceFaceCount { get; set; }
    public int PersonCount { get; set; }
    public List<FaceRecognitionComparisonResult> Results { get; set; } = [];
    public List<FaceRecognitionComparisonPair> Pairs { get; set; } = [];
}

public sealed class FaceRecognitionComparisonEmbedding
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required string FaceOccurrenceId { get; init; }
    public required string ModelId { get; init; }
    public required float[] Embedding { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}

public sealed class FaceRecognitionComparisonResult
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required string ModelId { get; init; }
    public required string ModelName { get; init; }
    public string ConfigurationJson { get; set; } = "{}";
    public string? Error { get; set; }
    public double? ElapsedMilliseconds { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public double? Threshold { get; set; }
    public int SamePersonPairs { get; set; }
    public int DifferentPersonPairs { get; set; }
    public int TruePositives { get; set; }
    public int FalsePositives { get; set; }
    public int TrueNegatives { get; set; }
    public int FalseNegatives { get; set; }
    public List<FaceRecognitionComparisonScore> Scores { get; set; } = [];
}

public sealed class FaceRecognitionComparisonPair
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required string FirstFaceOccurrenceId { get; init; }
    public required string SecondFaceOccurrenceId { get; init; }
    public required bool IsSamePerson { get; init; }
    public List<FaceRecognitionComparisonScore> Scores { get; set; } = [];
}

public sealed class FaceRecognitionComparisonScore
{
    public required string Id { get; init; }
    public required string PairId { get; init; }
    public required string ResultId { get; init; }
    public required double Score { get; init; }
    public required bool IsMatch { get; init; }
}
