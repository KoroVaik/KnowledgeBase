namespace KnowledgeBase.Core.FaceAnalysis;

public sealed record FaceRecognitionComparisonModel(string Id, string Name);

public static class FaceRecognitionComparisonModels
{
    public static readonly IReadOnlyList<FaceRecognitionComparisonModel> All =
    [
        new("arcface-r50", "ArcFace R50 · current"),
        new("arcface-mbf", "ArcFace MobileFaceNet"),
        new("faceonnx-resnet27", "FaceONNX ResNet27"),
    ];
}

public sealed record RecognitionComparisonPair(string FirstFaceOccurrenceId, string SecondFaceOccurrenceId, bool IsSamePerson, double Score);

public sealed record RecognitionComparisonMetrics(
    double Threshold,
    int SamePersonPairs,
    int DifferentPersonPairs,
    int TruePositives,
    int FalsePositives,
    int TrueNegatives,
    int FalseNegatives)
{
    public double Precision => TruePositives + FalsePositives == 0 ? 0 : (double)TruePositives / (TruePositives + FalsePositives);
    public double Recall => TruePositives + FalseNegatives == 0 ? 0 : (double)TruePositives / (TruePositives + FalseNegatives);
    public double F1 => Precision + Recall == 0 ? 0 : 2 * Precision * Recall / (Precision + Recall);
}

public static class FaceRecognitionComparison
{
    // The threshold is selected from reviewed references only. Sorting once and moving one score
    // group at a time avoids repeatedly scanning every pair as the reference set grows.
    public static RecognitionComparisonMetrics Calibrate(IReadOnlyList<RecognitionComparisonPair> pairs)
    {
        if (pairs.Count == 0) return new RecognitionComparisonMetrics(0, 0, 0, 0, 0, 0, 0);

        var same = pairs.Count(pair => pair.IsSamePerson);
        var different = pairs.Count - same;
        var truePositives = 0;
        var falsePositives = 0;
        var trueNegatives = different;
        var falseNegatives = same;
        var best = Metrics(double.PositiveInfinity, same, different, truePositives, falsePositives, trueNegatives, falseNegatives);

        foreach (var group in pairs.OrderByDescending(pair => pair.Score).GroupBy(pair => pair.Score))
        {
            foreach (var pair in group)
            {
                if (pair.IsSamePerson) { truePositives++; falseNegatives--; }
                else { falsePositives++; trueNegatives--; }
            }
            var candidate = Metrics(group.Key, same, different, truePositives, falsePositives, trueNegatives, falseNegatives);
            if (candidate.F1 > best.F1 || candidate.F1 == best.F1 && candidate.Recall > best.Recall)
                best = candidate;
        }

        return best;
    }

    public static bool IsMatch(double score, RecognitionComparisonMetrics metrics) => score >= metrics.Threshold;

    private static RecognitionComparisonMetrics Metrics(double threshold, int same, int different, int tp, int fp, int tn, int fn) =>
        new(threshold, same, different, tp, fp, tn, fn);
}
