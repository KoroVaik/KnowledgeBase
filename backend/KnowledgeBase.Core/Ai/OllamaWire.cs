using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace KnowledgeBase.Core.Ai;

// The wire shapes for Ollama's HTTP API. Keys are snake_case on the wire, hence the explicit
// names. Kept internal to this slice - nothing outside the analyzer speaks Ollama.

internal sealed record OllamaChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<OllamaMessage> Messages { get; init; }

    // Ollama streams token-by-token by default; we want one complete JSON body.
    [JsonPropertyName("stream")]
    public bool Stream => false;

    // A JSON schema here makes Ollama return content that matches it, instead of free prose
    // we would have to salvage.
    [JsonPropertyName("format")]
    public required JsonObject Format { get; init; }

    [JsonPropertyName("keep_alive")]
    public required string KeepAlive { get; init; }

    [JsonPropertyName("options")]
    public required JsonObject Options { get; init; }
}

internal sealed record OllamaMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("images")] IReadOnlyList<string>? Images = null);

internal sealed record OllamaChatResponse
{
    // The assistant turn. Its Content is itself a JSON string matching the requested schema.
    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; init; }

    // Why generation stopped. "stop" is a clean finish; "length" means it ran into the
    // context or token limit and the content is cut off - for our schema-constrained output
    // that is an unterminated JSON string, so we catch it before trying to parse it.
    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; init; }
}

internal sealed record OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public IReadOnlyList<OllamaTag> Models { get; init; } = [];
}

internal sealed record OllamaTag(
    [property: JsonPropertyName("name")] string Name);
