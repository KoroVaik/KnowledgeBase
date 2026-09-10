using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace KnowledgeBase.Core.Ai;

// Ollama's HTTP wire shapes. snake_case keys. Internal - only the analyzer speaks Ollama.

internal sealed record OllamaChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<OllamaMessage> Messages { get; init; }

    // Ollama streams by default; we want one complete body.
    [JsonPropertyName("stream")]
    public bool Stream => false;

    // A JSON schema makes Ollama return matching content, not free prose.
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
    // Content is itself a JSON string matching the requested schema.
    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; init; }

    // "length" = hit the context/token limit, so the JSON is cut - caught before parsing.
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
