namespace KnowledgeBase.Core.Pipeline;

public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    // How long the worker sleeps when the queue is empty before checking again (safety net if push notification is missed).
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromHours(1);

    // Wait, don't spin, while the analyzer is unavailable.
    public TimeSpan OutageDelay { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxAttempts { get; set; } = 3;

    // Ollama silently truncates a prompt over num_ctx, leaving the model no room to finish
    // the JSON. A source over this is marked Skipped, not failed. Map-reduce chunking is
    // the real fix (docs/ai-pipeline.md).
    public int MaxSourceChars { get; set; } = 12_000;

    // Same guard for a synthesis: the combined bodies of the notes being merged. Higher than
    // MaxSourceChars because merging is the whole point, lower than what would truncate.
    public int MaxSynthesisChars { get; set; } = 24_000;
}
