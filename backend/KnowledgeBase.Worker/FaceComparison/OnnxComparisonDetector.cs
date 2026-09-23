using KnowledgeBase.Core.FaceAnalysis;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace KnowledgeBase.Worker.FaceComparison;

public sealed class OnnxComparisonDetector : IDisposable
{
    public const int InputSize = 640;
    private readonly InferenceSession _session;
    private readonly bool _scrfd;
    private readonly float _threshold;
    private readonly float _nmsThreshold;

    public OnnxComparisonDetector(string path, bool scrfd, float threshold, float nmsThreshold)
    {
        using var sessionOptions = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
        _session = new InferenceSession(path, sessionOptions);
        _scrfd = scrfd;
        _threshold = threshold;
        _nmsThreshold = nmsThreshold;
    }

    public IReadOnlyList<ComparisonDetection> Detect(Image<Rgb24> image)
    {
        var scale = Math.Min((float)InputSize / image.Width, (float)InputSize / image.Height);
        var width = Math.Max(1, (int)(image.Width * scale));
        var height = Math.Max(1, (int)(image.Height * scale));
        using var resized = image.Clone(context => context.Resize(width, height, KnownResamplers.Triangle));
        var input = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        if (_scrfd) input.Buffer.Span.Fill(-127.5f / 128f);
        resized.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    var pixel = row[x];
                    input[0, 0, y, x] = _scrfd ? (pixel.R - 127.5f) / 128f : pixel.B;
                    input[0, 1, y, x] = _scrfd ? (pixel.G - 127.5f) / 128f : pixel.G;
                    input[0, 2, y, x] = _scrfd ? (pixel.B - 127.5f) / 128f : pixel.R;
                }
            }
        });
        using var output = _session.Run([NamedOnnxValue.CreateFromTensor(_session.InputMetadata.Keys.Single(), input)]);
        var tensors = output.ToDictionary(value => value.Name, value => value.AsTensor<float>().ToArray());
        var candidates = _scrfd ? DecodeScrfd(tensors) : DecodeYuNet(tensors);
        var kept = Suppress(candidates, _nmsThreshold);
        var scaleX = (float)width / image.Width;
        var scaleY = (float)height / image.Height;
        return kept.Select(face => new ComparisonDetection(
            new(face.Bounds.X / scaleX, face.Bounds.Y / scaleY, face.Bounds.Width / scaleX, face.Bounds.Height / scaleY),
            face.Score, face.Landmarks.Select(point => new ComparisonLandmark(point.X / scaleX, point.Y / scaleY)).ToArray())).ToArray();
    }

    private List<ComparisonDetection> DecodeScrfd(Dictionary<string, float[]> output)
    {
        var names = _session.OutputMetadata.Keys.ToArray();
        if (names.Length != 9) throw new InvalidDataException("Expected SCRFD's nine score, box and landmark outputs.");
        var faces = new List<ComparisonDetection>();
        for (var level = 0; level < 3; level++)
        {
            var stride = 8 << level;
            var columns = InputSize / stride;
            var scores = output[names[level]];
            var boxes = output[names[level + 3]];
            var points = output[names[level + 6]];
            if (scores.Length != columns * columns * 2 || boxes.Length != scores.Length * 4 || points.Length != scores.Length * 10)
                throw new InvalidDataException("Unexpected SCRFD output dimensions.");
            for (var index = 0; index < scores.Length; index++)
            {
                if (!float.IsFinite(scores[index]) || scores[index] < _threshold) continue;
                var cx = index / 2 % columns * stride;
                var cy = index / 2 / columns * stride;
                var left = boxes[index * 4] * stride;
                var top = boxes[index * 4 + 1] * stride;
                var right = boxes[index * 4 + 2] * stride;
                var bottom = boxes[index * 4 + 3] * stride;
                var landmarks = Enumerable.Range(0, 5).Select(point => new ComparisonLandmark(
                    cx + points[index * 10 + point * 2] * stride,
                    cy + points[index * 10 + point * 2 + 1] * stride)).ToArray();
                faces.Add(new(new(cx - left, cy - top, left + right, top + bottom), scores[index], landmarks));
            }
        }
        return faces;
    }

    private List<ComparisonDetection> DecodeYuNet(Dictionary<string, float[]> output)
    {
        var faces = new List<ComparisonDetection>();
        foreach (var stride in new[] { 8, 16, 32 })
        {
            var classes = output[$"cls_{stride}"];
            var objects = output[$"obj_{stride}"];
            var boxes = output[$"bbox_{stride}"];
            var points = output[$"kps_{stride}"];
            var columns = InputSize / stride;
            if (classes.Length != columns * columns || objects.Length != classes.Length || boxes.Length != classes.Length * 4 || points.Length != classes.Length * 10)
                throw new InvalidDataException("Unexpected YuNet output dimensions.");
            for (var index = 0; index < classes.Length; index++)
            {
                var score = MathF.Sqrt(Math.Clamp(classes[index], 0, 1) * Math.Clamp(objects[index], 0, 1));
                if (!float.IsFinite(score) || score < _threshold) continue;
                var column = index % columns;
                var row = index / columns;
                var cx = (column + boxes[index * 4]) * stride;
                var cy = (row + boxes[index * 4 + 1]) * stride;
                var width = MathF.Exp(boxes[index * 4 + 2]) * stride;
                var height = MathF.Exp(boxes[index * 4 + 3]) * stride;
                var landmarks = Enumerable.Range(0, 5).Select(point => new ComparisonLandmark(
                    (column + points[index * 10 + point * 2]) * stride,
                    (row + points[index * 10 + point * 2 + 1]) * stride)).ToArray();
                faces.Add(new(new(cx - width / 2, cy - height / 2, width, height), score, landmarks));
            }
        }
        return faces;
    }

    public static IReadOnlyList<ComparisonDetection> Suppress(IEnumerable<ComparisonDetection> faces, float threshold)
    {
        var kept = new List<ComparisonDetection>();
        foreach (var face in faces.Where(IsFinite).OrderByDescending(face => face.Score))
        {
            if (kept.Any(other => Overlap(face.Bounds, other.Bounds) > threshold)) continue;
            kept.Add(face);
        }
        return kept;
    }

    private static bool IsFinite(ComparisonDetection face) => double.IsFinite(face.Score)
        && float.IsFinite(face.Bounds.X) && float.IsFinite(face.Bounds.Y)
        && float.IsFinite(face.Bounds.Width) && float.IsFinite(face.Bounds.Height)
        && face.Bounds.Width > 0 && face.Bounds.Height > 0
        && face.Landmarks.All(point => float.IsFinite(point.X) && float.IsFinite(point.Y));

    private static float Overlap(ComparisonBounds first, ComparisonBounds second)
    {
        var width = Math.Max(0, Math.Min(first.X + first.Width, second.X + second.Width) - Math.Max(first.X, second.X));
        var height = Math.Max(0, Math.Min(first.Y + first.Height, second.Y + second.Height) - Math.Max(first.Y, second.Y));
        var intersection = width * height;
        return intersection / (first.Width * first.Height + second.Width * second.Height - intersection);
    }

    public void Dispose() => _session.Dispose();
}
