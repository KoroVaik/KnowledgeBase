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

    public async Task<AnalysisResult> AnalyzeAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        var images = request.Image is { } image
            ? new[] { Convert.ToBase64String(image.Bytes) }
            : null;

        var chat = new OllamaChatRequest
        {
            Model = _options.Model,
            KeepAlive = $"{(int)_options.KeepAlive.TotalSeconds}s",
            Format = ResultSchema(),
            Options = ModelOptions(),
            Messages =
            [
                new OllamaMessage("system", SystemPrompt),
                new OllamaMessage("user", UserPrompt(request), images),
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

        var result = JsonSerializer.Deserialize<AnalysisResult>(content, ResultJson);

        var tags = result?.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).ToList() ?? [];

        if (result is null || string.IsNullOrWhiteSpace(result.Title) || tags.Count == 0)
        {
            throw new InvalidOperationException($"Ollama returned an unusable analysis: {content}");
        }

        return result with { Tags = tags, Links = result.Links ?? [] };
    }

    private const string SystemPrompt =
        """
        You turn a source into a note for a personal knowledge base and draft it in Markdown.
        The source is either text or an image; for an image, transcribe any text in it and
        describe what it shows, then write the note from that.
        Return only JSON matching the schema.
        - title: a short, specific title for this note.
        - tags: 1 to 5 tags, most relevant first. Prefer tags from the known list the user
          provides; only invent a new one when nothing on the list fits, and never invent
          more than one new tag. Each tag is one or two words.
        - markdownBody: the note as clean Markdown. Stay faithful to the source and do not
          invent facts.
        - links: titles taken verbatim from the existing-notes list that this note is genuinely
          related to. Use [] when none apply. Never invent a title that is not in the list.
        """;

    private static string UserPrompt(AnalysisRequest request)
    {
        var knownTags = request.KnownTags.Count > 0
            ? string.Join(", ", request.KnownTags)
            : "(none yet)";

        var titles = request.ExistingTitles.Count > 0
            ? string.Join("\n", request.ExistingTitles.Select(title => $"- {title}"))
            : "(none yet)";

        var source = request.Text is { } text
            ? $"""
                Source text (between the markers):
                <<<BEGIN>>>
                {text}
                <<<END>>>
                """
            : "The source is the attached image.";

        return $"""
            Known tags: {knownTags}

            Existing notes:
            {titles}

            {source}
            """;
    }

    private static JsonObject ResultSchema() => new()
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
