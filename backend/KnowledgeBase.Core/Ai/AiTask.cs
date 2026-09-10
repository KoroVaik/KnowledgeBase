using System.Text.Json.Nodes;

namespace KnowledgeBase.Core.Ai;

// One structured-output call: the two prompts, the JSON schema the reply must match, and an
// optional image. Each pipeline builds its own - the analyzer knows nothing about them.
public sealed record AiTask(
    string SystemPrompt,
    string UserPrompt,
    JsonObject Schema,
    AnalysisImage? Image = null);
