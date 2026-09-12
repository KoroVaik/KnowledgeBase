namespace KnowledgeBase.Core.Persistence;

// A qualitative self-report from the model, not a calibrated probability - see Tag review and
// Tag hierarchy in docs/database.md. Three levels rather than a percentage: an LLM's own "87%"
// is not evidence of anything, so a coarse bucket is the honest way to surface it.
public enum SuggestionConfidence
{
    Low,
    Medium,
    High,
}

public static class SuggestionConfidenceParsing
{
    // Falls back to Medium rather than dropping the whole suggestion - the target name is still
    // useful even when the model's confidence field came back malformed.
    public static SuggestionConfidence Parse(string? value) =>
        Enum.TryParse<SuggestionConfidence>(value, ignoreCase: true, out var confidence) ? confidence : SuggestionConfidence.Medium;
}
