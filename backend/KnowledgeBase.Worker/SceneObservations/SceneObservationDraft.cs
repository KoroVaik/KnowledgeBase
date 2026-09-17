using System.Text.Json.Nodes;

namespace KnowledgeBase.Worker.SceneObservations;

public sealed record SceneObservationDraft(IReadOnlyList<SceneObservationDraftItem> Observations)
{
    public static JsonObject Schema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["observations"] = new JsonObject
            {
                ["type"] = "array",
                ["maxItems"] = 10,
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Action", "Interaction", "Object", "Text", "Mood") },
                        ["subjectPersonName"] = new JsonObject { ["type"] = "string" },
                        ["relatedPersonName"] = new JsonObject { ["type"] = "string" },
                        ["description"] = new JsonObject { ["type"] = "string" },
                        ["evidence"] = new JsonObject { ["type"] = "string" },
                        ["confidence"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
                    },
                    ["required"] = new JsonArray("kind", "subjectPersonName", "relatedPersonName", "description", "evidence", "confidence"),
                },
            },
        },
        ["required"] = new JsonArray("observations"),
    };
}

public sealed record SceneObservationDraftItem(
    string Kind,
    string SubjectPersonName,
    string RelatedPersonName,
    string Description,
    string Evidence,
    double Confidence);
