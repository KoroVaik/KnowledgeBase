using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Worker.FaceComparison;
using Xunit;

namespace KnowledgeBase.Tests;

public sealed class FaceComparisonTests
{
    [Fact]
    public void RecognitionCalibrationSelectsTheBestSeparatingThreshold()
    {
        var metrics = FaceRecognitionComparison.Calibrate([
            new("a", "b", true, 0.91), new("a", "c", true, 0.73),
            new("a", "d", false, 0.65), new("b", "d", false, 0.18),
        ]);

        Assert.Equal(0.73, metrics.Threshold);
        Assert.Equal(2, metrics.TruePositives);
        Assert.Equal(0, metrics.FalseNegatives);
        Assert.Equal(0, metrics.FalsePositives);
        Assert.True(FaceRecognitionComparison.IsMatch(0.73, metrics));
        Assert.False(FaceRecognitionComparison.IsMatch(0.65, metrics));
    }

    [Fact]
    public void BackgroundDetectionWithCoincidentEyesIsFlagged()
    {
        var face = new ComparisonDetection(new(550, 123, 14, 95), 0.9842512,
            [new(561, 164), new(561, 164), new(555, 180), new(560, 190), new(561, 191)]);
        var warnings = FaceComparisonQuality.Warnings(face, 564, 354);
        Assert.Contains("Eye landmarks overlap", warnings);
        Assert.Contains("Unusual box proportions", warnings);
        Assert.Equal(0.9842512, face.Score);
        Assert.False(FaceComparisonQuality.IsViableForRecognition(face, 564, 354));
    }

    [Fact]
    public void RealFaceInTheRegressionPhotoHasNoGeometryWarnings()
    {
        var face = new ComparisonDetection(new(224, 25, 89, 111), 0.9840946,
            [new(252, 60), new(291, 65), new(272, 77), new(250, 101), new(287, 106)]);
        Assert.Empty(FaceComparisonQuality.Warnings(face, 564, 354));
        Assert.True(FaceComparisonQuality.IsViableForRecognition(face, 564, 354));
    }

    [Fact]
    public void TiltedFaceDoesNotRequireHorizontalEyes()
    {
        var face = new ComparisonDetection(new(10, 10, 100, 100), 0.8,
            [new(30, 30), new(55, 60), new(40, 55), new(30, 65), new(45, 80)]);
        Assert.Empty(FaceComparisonQuality.Warnings(face, 200, 200));
    }

    [Fact]
    public void PartialFaceIsRetainedWithAnExplicitWarning()
    {
        var face = Detection(-10, 20, 90, 100, 0.9);
        Assert.Contains("Box extends beyond photo", FaceComparisonQuality.Warnings(face, 200, 200));
        Assert.True(FaceComparisonQuality.IsPartial(face, 200, 200));
        Assert.Single(OnnxComparisonDetector.Suppress([face], 0.4f));
    }

    [Fact]
    public void FaceTouchingPhotoEdgeIsMarkedPartialButIsStillViable()
    {
        var face = Detection(0, 40, 70, 80, 0.9);
        Assert.True(FaceComparisonQuality.IsPartial(face, 200, 200));
        Assert.True(FaceComparisonQuality.IsViableForRecognition(face, 200, 200));
    }

    [Fact]
    public void SuppressionKeepsBestOverlappingBoxAndSeparateFace()
    {
        var best = Detection(10, 10, 50, 60, 0.9);
        var duplicate = Detection(12, 12, 50, 60, 0.7);
        var neighbour = Detection(65, 10, 50, 60, 0.8);
        Assert.Equal([best, neighbour], OnnxComparisonDetector.Suppress([duplicate, neighbour, best], 0.4f));
    }

    [Fact]
    public void InvalidNumericPredictionsNeverReachJsonOrNms()
    {
        var valid = Detection(10, 10, 50, 60, 0.9);
        var result = OnnxComparisonDetector.Suppress([
            valid, Detection(float.NaN, 0, 50, 60, 0.95), Detection(0, 0, float.PositiveInfinity, 30, 0.95),
            Detection(0, 0, 0, 30, 0.95), Detection(0, 0, 30, 30, double.NaN),
        ], 0.4f);
        Assert.Equal(valid, Assert.Single(result));
    }

    private static ComparisonDetection Detection(float x, float y, float width, float height, double score) =>
        new(new(x, y, width, height), score, [new(20, 30), new(50, 30), new(35, 45), new(25, 60), new(45, 60)]);
}
