using System.Security.Cryptography;
using System.Text;
using ElBruno.LocalEmbeddings.ImageEmbeddings;
using KnowledgeBase.Core.SceneAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Worker.SceneAnalysis;

public sealed class ClipSceneEmbedder(IServiceProvider services, IOptions<VisualAnalysisOptions> options) : ISceneEmbedder
{
    private readonly VisualAnalysisOptions _options = options.Value;

    public string ModelKey => "CLIP vision encoder ONNX";

    public string ConfigurationHash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{ModelKey}|{_options.ModelDirectory}")));

    public Task<float[]> EmbedAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(imageBytes, writable: false);
        return Task.FromResult(services.GetRequiredService<ClipImageEncoder>().Encode(stream));
    }
}
