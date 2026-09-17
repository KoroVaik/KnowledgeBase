namespace KnowledgeBase.Core.FaceAnalysis;

public interface IFaceAnalyzer
{
    string ModelKey { get; }

    string ConfigurationHash { get; }

    Task<IReadOnlyList<DetectedFace>> AnalyzeAsync(byte[] imageBytes, CancellationToken cancellationToken);
}

public sealed record DetectedFace(
    int X,
    int Y,
    int Width,
    int Height,
    double DetectionScore,
    IReadOnlyList<FaceLandmark> Landmarks,
    float[] Embedding);

public sealed record FaceLandmark(float X, float Y);
