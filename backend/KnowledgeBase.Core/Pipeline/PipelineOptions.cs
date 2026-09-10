namespace KnowledgeBase.Core.Pipeline;

public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    // How long to wait before looking again once the analyzer is found unavailable - long
    // enough not to spin, short enough to pick up once Ollama is back.
    public TimeSpan OutageDelay { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxAttempts { get; set; } = 3;

    // The largest source, in characters, the worker will hand to the model. Ollama silently
    // truncates a prompt that overflows num_ctx, and with almost no room left the model runs
    // out of context before it finishes the JSON - the response comes back cut mid-string.
    // A source over this is marked Skipped instead of failing three times over. Rough guide:
    // ~4 chars per token for English, fewer for Cyrillic, so this leaves headroom in an
    // 8k context for the system prompt, the titles list and the model's own answer.
    // Chunking large documents (map-reduce) is the real fix - see CHECKLIST.
    public int MaxSourceChars { get; set; } = 12_000;
}
