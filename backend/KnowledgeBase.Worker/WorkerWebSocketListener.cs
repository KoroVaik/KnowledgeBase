using System.Net.WebSockets;
using System.Text;
using KnowledgeBase.Core.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Worker;

public sealed class WorkerWebSocketListener(
    IOptions<EventsBridgeOptions> bridgeOptions,
    IJobWakeSignal wakeSignal,
    ILogger<WorkerWebSocketListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = bridgeOptions.Value;
        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl) || string.IsNullOrWhiteSpace(options.IngestToken))
        {
            logger.LogInformation("Worker WebSocket listener disabled: ApiBaseUrl or IngestToken is missing.");
            return;
        }

        var baseUri = new Uri(options.ApiBaseUrl);
        var scheme = baseUri.Scheme == "https" ? "wss" : "ws";
        var wsUri = new UriBuilder(baseUri)
        {
            Scheme = scheme,
            Path = "/api/worker/ws",
            Query = $"token={Uri.EscapeDataString(options.IngestToken)}"
        }.Uri;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var client = new ClientWebSocket();
                client.Options.SetRequestHeader("X-Ingest-Token", options.IngestToken);
                client.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

                logger.LogInformation("Connecting to API WebSocket at {Uri}...", wsUri);
                await client.ConnectAsync(wsUri, stoppingToken);
                logger.LogInformation("Connected to API WebSocket. Listening for job wake signals.");

                var buffer = new byte[1024];
                while (client.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                {
                    var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.Count > 0)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        logger.LogInformation("Received job wake signal from API: {Signal}", message);
                        wakeSignal.Trigger();
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogWarning("API WebSocket connection lost ({Error}). Reconnecting in 10s...", error.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
