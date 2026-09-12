using System.Text.Json.Nodes;

namespace KnowledgeBase.Core.Pipeline.TagHierarchy;

// What the model returns for one placement pass: for each tag awaiting placement, the confirmed
// tag(s) it could go under.
public sealed record TagHierarchySuggestionResult(IReadOnlyList<TagPlacementSuggestion>? Suggestions)
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
                            ["parents"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["type"] = "string" },
                            },
                        },
                        ["required"] = new JsonArray("tag", "parents"),
                    },
                },
            },
            ["required"] = new JsonArray("suggestions"),
        };
}

public sealed record TagPlacementSuggestion(string Tag, IReadOnlyList<string>? Parents);
