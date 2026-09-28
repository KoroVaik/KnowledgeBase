using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.RealTime;

namespace KnowledgeBase.Api.Infrastructure.RealTime;

public sealed class WorkerWebSocketNotifier : IWorkerJobNotifier, IDisposable
{
    private readonly ConcurrentDictionary<WebSocket, byte> _sockets = new();
    private static readonly byte[] WakeSignalPayload = Encoding.UTF8.GetBytes("job");
    private readonly ILogger<WorkerWebSocketNotifier> _logger;
    private readonly ChangeSubscription _subscription;
    private readonly CancellationTokenSource _cts = new();

    public WorkerWebSocketNotifier(IChangeNotifier changeNotifier, ILogger<WorkerWebSocketNotifier> logger)
    {
        _logger = logger;
        _subscription = changeNotifier.Subscribe();
        _ = ListenForChangesAsync(_cts.Token);
    }

    public void Register(WebSocket socket)
    {
        _sockets.TryAdd(socket, 0);
        _logger.LogInformation("Worker WebSocket connected. Active workers: {Count}", _sockets.Count);
    }

    public void Unregister(WebSocket socket)
    {
        _sockets.TryRemove(socket, out _);
        _logger.LogInformation("Worker WebSocket disconnected. Active workers: {Count}", _sockets.Count);
    }

    public void NotifyJobQueued()
    {
        if (_sockets.IsEmpty)
        {
            return;
        }

        _logger.LogInformation("Broadcasting wake signal to {Count} active worker WebSocket(s)", _sockets.Count);
        foreach (var socket in _sockets.Keys)
        {
            if (socket.State == WebSocketState.Open)
            {
                _ = SendWakeSignalAsync(socket);
            }
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
                    // Assets or PhotoAnalysis created/updated indicate new processing work for the worker
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
            // Normal shutdown
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "Error listening to change notifications in WorkerWebSocketNotifier");
        }
    }

    private async Task SendWakeSignalAsync(WebSocket socket)
    {
        try
        {
            await socket.SendAsync(
                new ArraySegment<byte>(WakeSignalPayload),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);
        }
        catch (Exception error)
        {
            _logger.LogDebug(error, "Failed to send wake signal to worker WebSocket");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _subscription.Dispose();
    }
}
