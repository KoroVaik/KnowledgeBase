using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KnowledgeBase.Core.Ai.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Core.Ai;

public sealed class OllamaAnalyzer(HttpClient http, IOptions<OllamaOptions> options) : IContentAnalyzer
{
    private static readonly JsonSerializerOptions ResultJson = new(JsonSerializerDefaults.Web);

    // Drops the images array from a text request rather than sending images: null.
    private static readonly JsonSerializerOptions RequestJson =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly OllamaOptions _options = options.Value;

    public async Task EnsureModelAvailableAsync(CancellationToken cancellationToken)
    {
        OllamaTagsResponse? tags;

        try
        {
            tags = await http.GetFromJsonAsync<OllamaTagsResponse>("/api/tags", cancellationToken);
        }
        catch (HttpRequestException error)
        {
            throw new InvalidOperationException(
                $"Ollama is not reachable at {_options.BaseUrl}. Is it running?", error);
        }

        var installed = tags?.Models.Select(model => model.Name) ?? [];

        // Ollama reports "qwen2.5:14b"; a bare "qwen2.5" in config means the ":latest" tag.
        var wanted = _options.Model.Contains(':') ? _options.Model : $"{_options.Model}:latest";

        if (!installed.Contains(wanted))
        {
            throw new InvalidOperationException(
                $"Ollama model '{_options.Model}' is not pulled. Run: ollama pull {_options.Model}");
        }
    }

    public async Task<T> RunAsync<T>(AiTask task, CancellationToken cancellationToken)
        where T : class
    {
        var images = task.Image is { } image
            ? new[] { Convert.ToBase64String(image.Bytes) }
            : null;

        var chat = new OllamaChatRequest
        {
            Model = _options.Model,
            KeepAlive = $"{(int)_options.KeepAlive.TotalSeconds}s",
            Format = task.Schema,
            Options = ModelOptions(),
            Messages =
            [
                new OllamaMessage("system", task.SystemPrompt),
                new OllamaMessage("user", task.UserPrompt, images),
            ],
        };

        var response = await http.PostAsJsonAsync("/api/chat", chat, RequestJson, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken);
        var content = body?.Message?.Content;

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Ollama returned an empty response.");
        }

        // "length" = hit the context limit mid-generation, so the JSON is truncated. Fail it
        // clearly instead of burning every retry on a cryptic parse error.
        if (string.Equals(body!.DoneReason, "length", StringComparison.OrdinalIgnoreCase))
        {
            throw new ContentTooLargeException(
                "Ollama ran out of context before finishing the analysis - the source is too "
                + "large for the model's context window. Split it into smaller files.");
        }

        // The content is itself a JSON string matching task.Schema - hence the second parse.
        return JsonSerializer.Deserialize<T>(content, ResultJson)
            ?? throw new InvalidOperationException($"Ollama returned an unusable response: {content}");
    }

    private JsonObject ModelOptions()
    {
        var options = new JsonObject();

        if (_options.Options.Temperature is { } temperature)
        {
            options["temperature"] = temperature;
        }

        if (_options.Options.NumCtx is { } numCtx)
        {
            options["num_ctx"] = numCtx;
        }

        if (_options.Options.NumGpu is { } numGpu)
        {
            options["num_gpu"] = numGpu;
        }

        if (_options.Options.NumThread is { } numThread)
        {
            options["num_thread"] = numThread;
        }

        return options;
    }
}
