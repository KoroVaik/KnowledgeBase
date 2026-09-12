using System.Text.Json.Nodes;

namespace KnowledgeBase.Core.Pipeline.TagGrouping;

// What the model returns for one grouping pass: for each unreviewed tag, the other tag in the
// vocabulary (confirmed or itself still unreviewed) it means the same thing as, or "" when none
// does.
public sealed record TagGroupingResult(IReadOnlyList<TagMergeSuggestion>? Suggestions)
{
    public static JsonObject Schema() =>
        new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["suggestions"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["tag"] = new JsonObject { ["type"] = "string" },
                            ["closestMatchingTag"] = new JsonObject { ["type"] = "string" },
                        },
                        ["required"] = new JsonArray("tag", "closestMatchingTag"),
                    },
                },
            },
            ["required"] = new JsonArray("suggestions"),
        };
}

public sealed record TagMergeSuggestion(string Tag, string ClosestMatchingTag);
