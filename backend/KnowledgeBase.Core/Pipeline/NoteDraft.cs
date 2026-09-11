using System.Text.Json.Nodes;

namespace KnowledgeBase.Core.Pipeline;

// What the model returns for any note the pipeline writes - a source note or a synthesis.
// The handler still filters Tags and Links: a local model over-invents both whatever the
// prompt says (see docs/ai-pipeline.md). A synthesis is not asked for tags - it carries the
// one tag it was built from.
public sealed record NoteDraft(
    string Title,
    IReadOnlyList<string>? Tags,
    string MarkdownBody,
    IReadOnlyList<string> Links,
    string? ClosestKnownTag = null)
{
    // The JSON schema Ollama's structured output must match.
    public static JsonObject Schema(bool withTags)
    {
        var properties = new JsonObject
        {
            ["title"] = new JsonObject { ["type"] = "string" },
            ["markdownBody"] = new JsonObject { ["type"] = "string" },
            ["links"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
            },
        };

        var required = new JsonArray("title", "markdownBody", "links");

        if (withTags)
        {
            properties["tags"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["minItems"] = 1,
                ["maxItems"] = 5,
            };
            properties["closestKnownTag"] = new JsonObject { ["type"] = "string" };

            required.Add("tags");
            required.Add("closestKnownTag");
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
        };
    }
}
