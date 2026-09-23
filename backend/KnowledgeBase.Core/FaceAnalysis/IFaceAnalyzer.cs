namespace KnowledgeBase.Core.FaceAnalysis;

public interface IFaceAnalyzer
{
    string ModelKey { get; }

    /// <summary>Identity of the embedding model alone, stored per face occurrence: the detector
    /// and the embedder evolve separately, and a re-embed migrates occurrences by this key.</summary>
    string EmbeddingModelKey { get; }

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
    float[] Embedding,
    bool IsPartial);

public sealed record FaceLandmark(float X, float Y);
