using System.Text.Json.Nodes;

namespace KnowledgeBase.Core.Pipeline;

// What the model returns for any note the pipeline writes - a source note or a synthesis.
// The handler still filters Tags and Links: a local model over-invents both whatever the
// prompt says (see docs/ai-pipeline.md).
public sealed record NoteDraft(
    string Title,
    IReadOnlyList<string> Tags,
    string MarkdownBody,
    IReadOnlyList<string> Links)
{
    // The JSON schema Ollama's structured output must match. Shared by every prompt.
    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["title"] = new JsonObject { ["type"] = "string" },
            ["tags"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["minItems"] = 1,
                ["maxItems"] = 5,
            },
            ["markdownBody"] = new JsonObject { ["type"] = "string" },
            ["links"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
            },
        },
        ["required"] = new JsonArray("title", "tags", "markdownBody", "links"),
    };
}
