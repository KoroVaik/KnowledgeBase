namespace KnowledgeBase.Core.FaceAnalysis;

public sealed record ComparisonBounds(float X, float Y, float Width, float Height);
public sealed record ComparisonLandmark(float X, float Y);
public sealed record ComparisonDetection(ComparisonBounds Bounds, double Score, IReadOnlyList<ComparisonLandmark> Landmarks);
public sealed record ComparisonModel(string Id, string Name);

public static class FaceComparisonModels
{
    public static readonly IReadOnlyList<ComparisonModel> All =
    [
        new("yolov5s-face", "YOLOv5s-face · FaceONNX 4.1.1.3"),
        new("scrfd-10g", "SCRFD-10GF · InsightFace v0.7"),
        new("yunet", "YuNet · OpenCV 2023mar"),
    ];
}

public static class FaceComparisonQuality
{
    public static bool IsPartial(ComparisonDetection face, int width, int height)
    {
        var box = face.Bounds;
        return box.X <= 1 || box.Y <= 1 || box.X + box.Width >= width - 1 || box.Y + box.Height >= height - 1;
    }

    /// <summary>Rejects detector output that cannot be aligned safely for recognition. It is
    /// intentionally stricter than a comparison warning: a partial but geometrically coherent
    /// face may still be useful for detector evaluation, while degenerate landmarks never are.</summary>
    public static bool IsViableForRecognition(ComparisonDetection face, int width, int height) =>
        !Warnings(face, width, height).Any(warning => warning is
            "Unusual box proportions" or "Missing facial landmarks" or "Eye landmarks overlap" or "Landmarks outside photo");

    public static string[] Warnings(ComparisonDetection face, int width, int height)
    {
        var warnings = new List<string>();
        var box = face.Bounds;
        if (Math.Min(box.Width, box.Height) < 20) warnings.Add("Small detection");
        if (box.Width <= 0 || box.Height <= 0 || box.Width / box.Height is < 0.35f or > 2.5f)
            warnings.Add("Unusual box proportions");
        if (box.X < 0 || box.Y < 0 || box.X + box.Width > width || box.Y + box.Height > height)
            warnings.Add("Box extends beyond photo");
        if (face.Landmarks.Count != 5) warnings.Add("Missing facial landmarks");
        else
        {
            var eyeX = face.Landmarks[0].X - face.Landmarks[1].X;
            var eyeY = face.Landmarks[0].Y - face.Landmarks[1].Y;
            if (eyeX * eyeX + eyeY * eyeY < 4) warnings.Add("Eye landmarks overlap");
            if (face.Landmarks.Any(point => point.X < 0 || point.Y < 0 || point.X >= width || point.Y >= height))
                warnings.Add("Landmarks outside photo");
        }
        return warnings.ToArray();
    }
}
