namespace KnowledgeBase.Core.Ai.Configuration;

public sealed class OllamaOptions
{
    public const string SectionName = "Ai:Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    // repo:tag, like a Docker image. One multimodal model for text and images.
    public string Model { get; set; } = "qwen2.5vl:7b";

    // How long Ollama keeps the model in VRAM after a call.
    public TimeSpan KeepAlive { get; set; } = TimeSpan.FromMinutes(5);

    // A CPU model plus load time exceeds the default 100s HttpClient timeout.
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(3);

    public OllamaModelOptions Options { get; set; } = new();
}

// Ollama's request "options". Nullable: only the set ones are sent.
public sealed class OllamaModelOptions
{
    // Low, not zero: categorisation should be stable across runs.
    public double? Temperature { get; set; } = 0.2;

    // A long note overflows Ollama's default 4096 and is silently truncated.
    public int? NumCtx { get; set; } = 8192;

    // null = Ollama decides, 0 = pure CPU. The client can restrict the GPU, not add one.
    public int? NumGpu { get; set; }

    public int? NumThread { get; set; }
}
