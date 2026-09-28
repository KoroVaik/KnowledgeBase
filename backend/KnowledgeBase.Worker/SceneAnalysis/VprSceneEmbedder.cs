using System.Security.Cryptography;
using System.Text;
using KnowledgeBase.Core.SceneAnalysis;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace KnowledgeBase.Worker.SceneAnalysis;

public sealed class VprSceneEmbedder : ISceneEmbedder, IDisposable
{
    private const int InputSize = 512;
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std  = [0.229f, 0.224f, 0.225f];

    private readonly InferenceSession _session;
    private readonly string _modelKey;
    private readonly string _configHash;

    public VprSceneEmbedder(IOptions<VisualAnalysisOptions> options)
    {
        var opts = options.Value;
        var modelDirectory = opts.ModelDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KnowledgeBase", "models", "eigenplaces");
        var modelPath = Path.Combine(modelDirectory, "eigenplaces_resnet50_512.onnx");

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(
                $"EigenPlaces model was not found at '{modelPath}'. Run tools/export_eigenplaces_onnx.py to export the model.",
                modelPath);
        }

        _session = new InferenceSession(modelPath);
        _modelKey = "EigenPlaces ResNet50 512-d ONNX";
        _configHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{_modelKey}|{modelPath}")));
    }

    public string ModelKey => _modelKey;
    public string ConfigurationHash => _configHash;

    public Task<float[]> EmbedAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var image = Image.Load<Rgb24>(imageBytes);
        image.Mutate(ctx => ctx.AutoOrient());
        image.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(InputSize, InputSize),
            Mode = ResizeMode.Stretch
        }));

        var tensor = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < InputSize; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < InputSize; x++)
                {
                    var pixel = row[x];
                    tensor[0, 0, y, x] = (pixel.R / 255f - Mean[0]) / Std[0];
                    tensor[0, 1, y, x] = (pixel.G / 255f - Mean[1]) / Std[1];
                    tensor[0, 2, y, x] = (pixel.B / 255f - Mean[2]) / Std[2];
                }
            }
        });

        using var results = _session.Run([
            NamedOnnxValue.CreateFromTensor("image", tensor)
        ]);
        var output = results.First().AsEnumerable<float>().ToArray();

        var sumSq = 0.0;
        for (var i = 0; i < output.Length; i++) sumSq += output[i] * output[i];
        var norm = Math.Sqrt(sumSq);
        if (norm > 0)
        {
            for (var i = 0; i < output.Length; i++) output[i] = (float)(output[i] / norm);
        }

        return Task.FromResult(output);
    }

    public void Dispose() => _session.Dispose();
}
