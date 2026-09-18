using System.Security.Cryptography;
using System.Text;
using FaceONNX;
using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.FaceAnalysis;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace KnowledgeBase.Worker.FaceAnalysis;

public sealed class FaceOnnxFaceAnalyzer(IOptions<FaceAnalysisOptions> options) : IFaceAnalyzer
{
    private readonly FaceAnalysisOptions _options = options.Value;

    public string ModelKey => "FaceONNX 4.1.1.3: YOLOv5s-face + ResNet27";

    public string ConfigurationHash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{ModelKey}|{_options.DetectionThreshold}|{_options.ConfidenceThreshold}|{_options.NonMaximumSuppressionThreshold}")));

    public Task<IReadOnlyList<DetectedFace>> AnalyzeAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var image = Image.Load<Rgb24>(imageBytes);
        // A phone stores the sensor's sideways pixels plus an EXIF rotation that the browser applies and
        // ImageSharp does not: without this, boxes land in a frame the review screen never shows.
        image.Mutate(context => context.AutoOrient());
        var pixels = ToBgrFloatArray(image);
        using var detector = new FaceDetector(_options.DetectionThreshold, _options.ConfidenceThreshold, _options.NonMaximumSuppressionThreshold);
        using var embedder = new FaceEmbedder();

        var faces = detector.Forward(pixels).Select(face => new DetectedFace(
            face.Rectangle.X, face.Rectangle.Y, face.Rectangle.Width, face.Rectangle.Height, face.Score,
            face.Points.All.Select(point => new FaceLandmark(point.X, point.Y)).ToList(),
            embedder.Forward(pixels.Align(face.Box, face.Points.RotationAngle)))).ToList();

        return Task.FromResult<IReadOnlyList<DetectedFace>>(faces);
    }

    private static float[][,] ToBgrFloatArray(Image<Rgb24> image)
    {
        var values = new[] { new float[image.Height, image.Width], new float[image.Height, image.Width], new float[image.Height, image.Width] };
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    values[2][y, x] = row[x].R / 255f;
                    values[1][y, x] = row[x].G / 255f;
                    values[0][y, x] = row[x].B / 255f;
                }
            }
        });
        return values;
    }
}
