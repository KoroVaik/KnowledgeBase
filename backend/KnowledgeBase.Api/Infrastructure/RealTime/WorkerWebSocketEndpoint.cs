using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using KnowledgeBase.Api.Controllers.Events.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Api.Infrastructure.RealTime;

public static class WorkerWebSocketEndpoint
{
    public static IEndpointRouteBuilder MapWorkerWebSocket(this IEndpointRouteBuilder endpoints)
    {
        endpoints.Map("/api/worker/ws", async (
            HttpContext context,
            WorkerWebSocketNotifier notifier,
            IOptions<EventsOptions> options,
            ILoggerFactory loggerFactory) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var token = context.Request.Headers["X-Ingest-Token"].ToString();
            if (string.IsNullOrEmpty(token))
            {
                token = context.Request.Query["token"].ToString();
            }

            var expected = options.Value.IngestToken;
            if (string.IsNullOrEmpty(expected) ||
                string.IsNullOrEmpty(token) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected)))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            notifier.Register(webSocket);
            var logger = loggerFactory.CreateLogger("WorkerWebSocket");

            var buffer = new byte[1024];
            try
            {
                while (webSocket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", context.RequestAborted);
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal client disconnect or server shutdown.
            }
            catch (Exception error)
            {
                logger.LogDebug(error, "Worker WebSocket disconnected");
            }
            finally
            {
                notifier.Unregister(webSocket);
            }
        });

        return endpoints;
    }
}
