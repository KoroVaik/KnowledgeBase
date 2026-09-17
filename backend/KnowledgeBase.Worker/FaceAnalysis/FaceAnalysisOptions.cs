namespace KnowledgeBase.Worker.FaceAnalysis;

public sealed class FaceAnalysisOptions
{
    public const string SectionName = "FaceAnalysis";

    public float DetectionThreshold { get; set; } = 0.3f;

    public float ConfidenceThreshold { get; set; } = 0.8f;

    public float NonMaximumSuppressionThreshold { get; set; } = 0.3f;
}
