namespace KnowledgeBase.Core.FaceAnalysis;

/// <summary>Maps a raw cosine similarity between face embeddings to a 0-1 confidence for display.
/// Center sits halfway between the measured same-person and different-person score averages and
/// Scale stretches the gap; the stored score stays raw evidence, this is only the read-time scale.</summary>
public sealed class FaceConfidenceCalibration
{
    public const string SectionName = "FaceAnalysis";

    public double Center { get; set; } = 0.28;

    public double Scale { get; set; } = 0.05;

    public double ToConfidence(double score) =>
        1.0 / (1.0 + Math.Exp(-(score - Center) / Math.Max(Scale, 1e-6)));
}
