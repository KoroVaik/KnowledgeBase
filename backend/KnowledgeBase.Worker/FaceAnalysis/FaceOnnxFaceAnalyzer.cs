using System.Security.Cryptography;
using System.Text;
using FaceONNX;
using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Worker.FaceComparison;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace KnowledgeBase.Worker.FaceAnalysis;

public sealed class FaceOnnxFaceAnalyzer(
    ArcFaceEmbedder embedder,
    IOptions<FaceAnalysisOptions> options,
    ComparisonModelFiles? modelFiles = null) : IFaceAnalyzer, IDisposable
{
    private readonly FaceAnalysisOptions _options = options.Value;
    private readonly SemaphoreSlim _verifierLock = new(1, 1);
    private OnnxComparisonDetector? _verifier;
    private bool _verifierInitAttempted;

    public string EmbeddingModelKey => "insightface w600k_r50 (ArcFace)";

    public string ModelKey => $"FaceONNX 4.1.1.3: YOLOv5s-face + {EmbeddingModelKey} + SCRFD verify";

    public string ConfigurationHash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{ModelKey}|{_options.DetectionThreshold}|{_options.ConfidenceThreshold}|{_options.NonMaximumSuppressionThreshold}|face-geometry-v1|scrfd-verify-v1")));

    public void Dispose()
    {
        embedder.Dispose();
        _verifier?.Dispose();
        _verifierLock.Dispose();
    }

    public async Task<IReadOnlyList<DetectedFace>> AnalyzeAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var image = Image.Load<Rgb24>(imageBytes);
        // A phone stores the sensor's sideways pixels plus an EXIF rotation that the browser applies and
        // ImageSharp does not: without this, boxes land in a frame the review screen never shows.
        image.Mutate(context => context.AutoOrient());
        var pixels = ToBgrFloatArray(image);
        using var detector = new FaceDetector(_options.DetectionThreshold, _options.ConfidenceThreshold, _options.NonMaximumSuppressionThreshold);

        var viableDetections = detector.Forward(pixels).Select(face =>
        {
            var landmarks = face.Points.All.Select(point => new FaceLandmark(point.X, point.Y)).ToList();
            var detection = new ComparisonDetection(
                new ComparisonBounds(face.Rectangle.X, face.Rectangle.Y, face.Rectangle.Width, face.Rectangle.Height),
                face.Score, landmarks.Select(point => new ComparisonLandmark(point.X, point.Y)).ToList());
            return (face, landmarks, detection);
        }).Where(result => FaceComparisonQuality.IsViableForRecognition(result.detection, image.Width, image.Height))
          .ToList();

        var verifier = await GetVerifierAsync(cancellationToken);

        var faces = viableDetections.Select(result =>
        {
            var faceRect = new Rectangle(result.face.Rectangle.X, result.face.Rectangle.Y, result.face.Rectangle.Width, result.face.Rectangle.Height);
            var verified = verifier is null || VerifyCrop(verifier, image, faceRect);
            return new DetectedFace(
                result.face.Rectangle.X, result.face.Rectangle.Y, result.face.Rectangle.Width, result.face.Rectangle.Height,
                result.face.Score, result.landmarks, embedder.Embed(image, result.landmarks),
                FaceComparisonQuality.IsPartial(result.detection, image.Width, image.Height),
                NeedsReview: !verified);
        }).ToList();

        return faces;
    }

    private static bool VerifyCrop(OnnxComparisonDetector verifier, Image<Rgb24> image, Rectangle faceRect)
    {
        var centerX = faceRect.X + faceRect.Width / 2f;
        var centerY = faceRect.Y + faceRect.Height / 2f;
        var padWidth = faceRect.Width * 2;
        var padHeight = faceRect.Height * 2;
        var cropX = Math.Max(0, (int)Math.Round(centerX - padWidth / 2f));
        var cropY = Math.Max(0, (int)Math.Round(centerY - padHeight / 2f));
        var cropWidth = Math.Min(image.Width - cropX, (int)Math.Round((float)padWidth));
        var cropHeight = Math.Min(image.Height - cropY, (int)Math.Round((float)padHeight));
        if (cropWidth <= 0 || cropHeight <= 0) return false;

        using var crop = image.Clone(ctx => ctx.Crop(new Rectangle(cropX, cropY, cropWidth, cropHeight)));
        var detections = verifier.Detect(crop);
        var targetBoxInCrop = new Rectangle(faceRect.X - cropX, faceRect.Y - cropY, faceRect.Width, faceRect.Height);
        return detections.Any(det =>
        {
            var detRect = new Rectangle((int)det.Bounds.X, (int)det.Bounds.Y, (int)det.Bounds.Width, (int)det.Bounds.Height);
            return detRect.IntersectsWith(targetBoxInCrop);
        });
    }

    private async Task<OnnxComparisonDetector?> GetVerifierAsync(CancellationToken cancellationToken)
    {
        if (_verifier is not null) return _verifier;
        if (modelFiles is null || _verifierInitAttempted) return _verifier;
        await _verifierLock.WaitAsync(cancellationToken);
        try
        {
            if (_verifier is not null || _verifierInitAttempted) return _verifier;
            _verifierInitAttempted = true;
            var path = await modelFiles.EnsureAsync("scrfd-10g", cancellationToken);
            _verifier = new OnnxComparisonDetector(path, scrfd: true, threshold: 0.5f, nmsThreshold: 0.4f);
            return _verifier;
        }
        catch
        {
            return null;
        }
        finally
        {
            _verifierLock.Release();
        }
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
