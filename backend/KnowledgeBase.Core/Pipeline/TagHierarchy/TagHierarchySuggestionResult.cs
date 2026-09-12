using System.Text.Json.Nodes;

namespace KnowledgeBase.Core.Pipeline.TagHierarchy;

// What the model returns for one placement pass: for each tag awaiting placement, the tag(s) it
// could go under, each with how sure the model is about that placement.
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
                                ["items"] = new JsonObject
                                {
                                    ["type"] = "object",
                                    ["properties"] = new JsonObject
                                    {
                                        ["name"] = new JsonObject { ["type"] = "string" },
                                        ["confidence"] = new JsonObject
                                        {
                                            ["type"] = "string",
                                            ["enum"] = new JsonArray("low", "medium", "high"),
                                        },
                                    },
                                    ["required"] = new JsonArray("name", "confidence"),
                                },
                            },
                        },
                        ["required"] = new JsonArray("tag", "parents"),
                    },
                },
            },
            ["required"] = new JsonArray("suggestions"),
        };
}

public sealed record TagPlacementSuggestion(string Tag, IReadOnlyList<TagParentCandidate>? Parents);

public sealed record TagParentCandidate(string Name, string? Confidence);
