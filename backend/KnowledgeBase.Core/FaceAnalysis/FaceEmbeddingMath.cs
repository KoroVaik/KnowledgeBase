namespace KnowledgeBase.Core.FaceAnalysis;

// The one similarity measure every face comparison uses: cosine over the stored embeddings.
public static class FaceEmbeddingMath
{
    public static double CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length != right.Length) throw new InvalidOperationException("Face embeddings from different models cannot be compared.");
        double dot = 0, leftLength = 0, rightLength = 0;
        for (var index = 0; index < left.Length; index++) { dot += left[index] * right[index]; leftLength += left[index] * left[index]; rightLength += right[index] * right[index]; }
        return leftLength == 0 || rightLength == 0 ? 0 : dot / Math.Sqrt(leftLength * rightLength);
    }
}
