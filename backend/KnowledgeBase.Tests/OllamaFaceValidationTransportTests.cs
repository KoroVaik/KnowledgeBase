using System.Net;
using System.Text;
using System.Text.Json;
using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Ai.Configuration;
using KnowledgeBase.Worker.FaceAnalysis;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeBase.Tests;

public sealed class OllamaFaceValidationTransportTests
{
    [Fact]
    public async Task SendsTargetAndContextInOrderWithoutChangingDefaultGenerationForOtherTasks()
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var analyzer = new OllamaAnalyzer(http, Options.Create(new OllamaOptions()));
        await analyzer.RunAsync<FaceValidationAnswer>(new AiTask("system", "user", FaceValidationPrompt.Schema(),
            new AnalysisImage([1, 2], "image/png"), [new AnalysisImage([3, 4], "image/png")], FaceValidationPrompt.Generation), CancellationToken.None);
        using var first = JsonDocument.Parse(handler.Requests[0]);
        var message = first.RootElement.GetProperty("messages")[1];
        Assert.Equal(new byte[] { 1, 2 }, Convert.FromBase64String(message.GetProperty("images")[0].GetString()!));
        Assert.Equal(new byte[] { 3, 4 }, Convert.FromBase64String(message.GetProperty("images")[1].GetString()!));
        Assert.Equal(0, first.RootElement.GetProperty("options").GetProperty("temperature").GetDouble());
        await analyzer.RunAsync<FaceValidationAnswer>(new AiTask("system", "user", FaceValidationPrompt.Schema()), CancellationToken.None);
        using var second = JsonDocument.Parse(handler.Requests[1]);
        Assert.False(second.RootElement.GetProperty("messages")[1].TryGetProperty("images", out _));
        Assert.Equal(.2, second.RootElement.GetProperty("options").GetProperty("temperature").GetDouble());
        Assert.False(second.RootElement.GetProperty("options").TryGetProperty("num_predict", out _));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                JsonSerializer.Serialize(new { done = true, done_reason = "stop", message = new { content = "{\"subject\":\"human_face\",\"evidence\":\"Visible facial features.\"}" } }), Encoding.UTF8, "application/json") };
        }
    }
}
