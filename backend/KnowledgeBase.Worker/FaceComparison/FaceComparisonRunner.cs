using System.Diagnostics;
using System.Text.Json;
using FaceONNX;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Worker.FaceAnalysis;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace KnowledgeBase.Worker.FaceComparison;

public sealed record DetectorComparisonOutput(string ConfigurationJson, double ElapsedMilliseconds, IReadOnlyList<ComparisonDetection> Detections);

public sealed class FaceComparisonRunner(ComparisonModelFiles files, IOptions<FaceComparisonOptions> options, IOptions<FaceAnalysisOptions> faceOptions)
{
    public async Task<DetectorComparisonOutput> RunAsync(string modelId, Image<Rgb24> image, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (modelId == "yolov5s-face")
        {
            var current = faceOptions.Value;
            using var sessionOptions = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
            using var detector = new FaceDetector(sessionOptions, current.DetectionThreshold, current.ConfidenceThreshold, current.NonMaximumSuppressionThreshold);
            var config = JsonSerializer.Serialize(new { adapter = "face-comparison/v1", inputSize = 640, cpuThreads = 2,
                objectnessThreshold = current.DetectionThreshold, classThreshold = current.ConfidenceThreshold,
                nmsThreshold = current.NonMaximumSuppressionThreshold, scoreMeaning = "Class score only; objectness is a separate gate" });
            return Measure(() => DetectYolo(detector, image), config, cancellationToken);
        }
        if (modelId is not ("scrfd-10g" or "yunet")) throw new ArgumentOutOfRangeException(nameof(modelId));
        var path = await files.EnsureAsync(modelId, cancellationToken);
        var threshold = modelId == "scrfd-10g" ? settings.ScrfdThreshold : settings.YuNetThreshold;
        using var onnx = new OnnxComparisonDetector(path, modelId == "scrfd-10g", threshold, settings.NmsThreshold);
        var configuration = JsonSerializer.Serialize(new { adapter = "face-comparison/v1", inputSize = 640, cpuThreads = 2,
            threshold, nmsThreshold = settings.NmsThreshold, modelSha256 = ComparisonModelFiles.HashFor(modelId),
            scoreMeaning = modelId == "scrfd-10g" ? "Face detection score" : "Square root of class score times objectness" });
        return Measure(() => onnx.Detect(image), configuration, cancellationToken);
    }

    private static DetectorComparisonOutput Measure(Func<IReadOnlyList<ComparisonDetection>> detect, string config, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        detect();
        var times = new List<double>();
        IReadOnlyList<ComparisonDetection> detections = [];
        for (var sample = 0; sample < 3; sample++)
        {
            token.ThrowIfCancellationRequested();
            var watch = Stopwatch.StartNew();
            detections = detect();
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        return new(config, times.Order().ElementAt(1), detections);
    }

    private static IReadOnlyList<ComparisonDetection> DetectYolo(FaceDetector detector, Image<Rgb24> image)
    {
        var pixels = new[] { new float[image.Height, image.Width], new float[image.Height, image.Width], new float[image.Height, image.Width] };
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    pixels[0][y, x] = row[x].B / 255f;
                    pixels[1][y, x] = row[x].G / 255f;
                    pixels[2][y, x] = row[x].R / 255f;
                }
            }
        });
        return detector.Forward(pixels).Select(face => new ComparisonDetection(
            new(face.Rectangle.X, face.Rectangle.Y, face.Rectangle.Width, face.Rectangle.Height), face.Score,
            face.Points.All.Select(point => new ComparisonLandmark(point.X, point.Y)).ToArray())).ToArray();
    }
}
