using KnowledgeBase.Api.Controllers.Events.Configuration;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.RealTime;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Api.Infrastructure.RealTime;

public sealed class WorkerWebhookNotifier : IWorkerJobNotifier, IDisposable
{
    public const string HttpClientName = "WorkerWake";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<EventsOptions> _options;
    private readonly ILogger<WorkerWebhookNotifier> _logger;
    private readonly ChangeSubscription _subscription;
    private readonly CancellationTokenSource _cts = new();

    public WorkerWebhookNotifier(
        IHttpClientFactory httpClientFactory,
        IOptions<EventsOptions> options,
        IChangeNotifier changeNotifier,
        ILogger<WorkerWebhookNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
        _subscription = changeNotifier.Subscribe();
        _ = ListenForChangesAsync(_cts.Token);
    }

    public void NotifyJobQueued()
    {
        var url = _options.Value.WorkerWakeUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        _ = SendWakeAsync(url);
    }

    private async Task SendWakeAsync(string url)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            var token = _options.Value.IngestToken;
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Add("X-Ingest-Token", token);
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await client.SendAsync(request, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully sent wake signal to worker at {Url}", url);
            }
            else
            {
                _logger.LogWarning("Worker wake signal returned {StatusCode} from {Url}", response.StatusCode, url);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send wake signal to worker at {Url} (worker may be offline)", url);
        }
    }

    private async Task ListenForChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   await _subscription.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_subscription.Reader.TryRead(out var change))
                {
                    if (change.Resource is ChangeResources.Assets or ChangeResources.PhotoAnalysis &&
                        change.Action is not ChangeActions.Deleted)
                    {
                        NotifyJobQueued();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "Error listening to change notifications in WorkerWebhookNotifier");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _subscription.Dispose();
    }
}
