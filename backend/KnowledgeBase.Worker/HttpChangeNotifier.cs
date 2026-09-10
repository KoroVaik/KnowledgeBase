using System.Net.Http.Json;
using KnowledgeBase.Core.RealTime;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Worker;

// The worker cannot reach the API's in-memory notifier, so it posts each change hint over
// HTTP; the API fans it out over SSE. See docs/worker.md.
public sealed class HttpChangeNotifier(
    IHttpClientFactory clientFactory,
    IOptions<EventsBridgeOptions> options,
    ILogger<HttpChangeNotifier> logger) : IChangeNotifier
{
    public const string ClientName = "events-bridge";

    private readonly EventsBridgeOptions _options = options.Value;

    public void Publish(ChangeEvent change)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiBaseUrl) || string.IsNullOrWhiteSpace(_options.IngestToken))
        {
            return;
        }

        // Fire-and-forget: the note is already committed, so a failed ping costs only a missed
        // live update - the browser picks it up on reconnect.
        _ = SendAsync(change);
    }

    private async Task SendAsync(ChangeEvent change)
    {
        try
        {
            using var client = clientFactory.CreateClient(ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/events/ingest")
            {
                Content = JsonContent.Create(change),
            };
            request.Headers.Add("X-Ingest-Token", _options.IngestToken);

            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Change hint ({Resource}/{Action}) rejected by the API: {Status}.",
                    change.Resource,
                    change.Action,
                    (int)response.StatusCode);
            }
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Change hint to the API failed.");
        }
    }

    public ChangeSubscription Subscribe() =>
        throw new NotSupportedException("The worker does not serve event streams.");
}
