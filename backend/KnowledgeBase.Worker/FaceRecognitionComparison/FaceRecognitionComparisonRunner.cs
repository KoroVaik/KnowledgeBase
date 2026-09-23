using System.Diagnostics;
using System.Text.Json;
using FaceONNX;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Worker.FaceAnalysis;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace KnowledgeBase.Worker.FaceRecognitionComparison;

public sealed record RecognitionComparisonFaceInput(
    string OccurrenceId,
    string AssetId,
    int X,
    int Y,
    int Width,
    int Height,
    IReadOnlyList<FaceLandmark> Landmarks,
    Image<Rgb24> Image);

public sealed record RecognitionComparisonOutput(string ConfigurationJson, double ElapsedMilliseconds, IReadOnlyDictionary<string, float[]> Embeddings);

public sealed class FaceRecognitionComparisonRunner(
    RecognitionComparisonModelFiles files,
    IOptions<FaceAnalysisOptions> faceOptions)
{
    public async Task<RecognitionComparisonOutput> RunAsync(
        string modelId, IReadOnlyList<RecognitionComparisonFaceInput> faces, CancellationToken cancellationToken)
    {
        return modelId switch
        {
            "arcface-r50" => await RunArcFaceAsync(ResolveCurrentModelPath(faceOptions.Value), "insightface/w600k_r50", faces, cancellationToken),
            "arcface-mbf" => await RunArcFaceAsync(await files.EnsureMobileFaceNetAsync(cancellationToken), "insightface/w600k_mbf", faces, cancellationToken),
            "faceonnx-resnet27" => RunFaceOnnx(faces, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(modelId)),
        };
    }

    private static Task<RecognitionComparisonOutput> RunArcFaceAsync(
        string modelPath, string model, IReadOnlyList<RecognitionComparisonFaceInput> faces, CancellationToken cancellationToken)
    {
        using var embedder = new ArcFaceEmbedder(modelPath);
        var watch = Stopwatch.StartNew();
        var embeddings = new Dictionary<string, float[]>(faces.Count);
        foreach (var face in faces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            embeddings.Add(face.OccurrenceId, embedder.Embed(face.Image, face.Landmarks));
        }
        return Task.FromResult(new RecognitionComparisonOutput(
            JsonSerializer.Serialize(new { adapter = "face-recognition-comparison/v1", model, alignment = "five-point similarity", inputSize = 112, cpuThreads = 0 }),
            watch.Elapsed.TotalMilliseconds, embeddings));
    }

    private static RecognitionComparisonOutput RunFaceOnnx(IReadOnlyList<RecognitionComparisonFaceInput> faces, CancellationToken cancellationToken)
    {
        using var embedder = new FaceEmbedder();
        var bgrByAsset = new Dictionary<string, float[][,]>();
        var watch = Stopwatch.StartNew();
        var embeddings = new Dictionary<string, float[]>(faces.Count);
        foreach (var face in faces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!bgrByAsset.TryGetValue(face.AssetId, out var pixels))
            {
                pixels = ToBgrFloatArray(face.Image);
                bgrByAsset.Add(face.AssetId, pixels);
            }
            var landmarks = new Face5Landmarks(face.Landmarks
                .Select(point => new System.Drawing.Point((int)MathF.Round(point.X), (int)MathF.Round(point.Y))).ToArray());
            var bounds = new System.Drawing.Rectangle(face.X, face.Y, face.Width, face.Height);
            embeddings.Add(face.OccurrenceId, embedder.Forward(pixels.Align(bounds, landmarks.RotationAngle)));
        }
        return new RecognitionComparisonOutput(
            JsonSerializer.Serialize(new { adapter = "face-recognition-comparison/v1", model = "FaceONNX/recognition_resnet27", alignment = "detector eye-angle", inputSize = 128, cpuThreads = 0 }),
            watch.Elapsed.TotalMilliseconds, embeddings);
    }

    private static string ResolveCurrentModelPath(FaceAnalysisOptions options)
    {
        var configured = options.RecognitionModelPath;
        if (Path.IsPathRooted(configured) && File.Exists(configured)) return configured;
        var candidates = new[]
        {
            Path.GetFullPath(configured),
            Path.Combine(AppContext.BaseDirectory, configured),
            Path.Combine(Directory.GetCurrentDirectory(), configured),
            Path.Combine(Directory.GetCurrentDirectory(), "backend", "KnowledgeBase.Worker", configured),
        };
        return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException(
            $"The current ArcFace model was not found. Looked at: {string.Join("; ", candidates.Distinct())}.", configured);
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
