namespace KnowledgeBase.Core.Ai.Configuration;

public sealed class OllamaOptions
{
    public const string SectionName = "Ai:Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    // Named like a Docker image (repo:tag). /api/tags lists what is pulled locally. One
    // multimodal model handles both text and images for now.
    public string Model { get; set; } = "qwen2.5vl:7b";

    // How long Ollama keeps the model in VRAM after a call. The first request after that
    // window pays the load time again - fine for a background pipeline.
    public TimeSpan KeepAlive { get; set; } = TimeSpan.FromMinutes(5);

    // A local model on CPU can take tens of seconds; the default HttpClient 100s is not enough
    // once the model also has to load.
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(3);

    public OllamaModelOptions Options { get; set; } = new();
}

// Passed straight through as Ollama's request "options". All nullable: only the ones set here
// are sent, everything else stays at Ollama's own defaults.
public sealed class OllamaModelOptions
{
    // Low, not zero: categorisation should land on the same answer across runs.
    public double? Temperature { get; set; } = 0.2;

    // Context window in tokens. A long note can overflow Ollama's default 4096 and get
    // silently truncated.
    public int? NumCtx { get; set; } = 8192;

    // Model layers to place on the GPU. null = let Ollama decide (it offloads what fits VRAM),
    // 0 = force pure CPU. The client can restrict the GPU, not add one.
    public int? NumGpu { get; set; }

    public int? NumThread { get; set; }
}
