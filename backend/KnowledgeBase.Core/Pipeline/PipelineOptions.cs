namespace KnowledgeBase.Core.Pipeline;

public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    // How long to wait before looking again once the analyzer is found unavailable - long
    // enough not to spin, short enough to pick up once Ollama is back.
    public TimeSpan OutageDelay { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxAttempts { get; set; } = 3;
}
