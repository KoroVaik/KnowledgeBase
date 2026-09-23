using System.Diagnostics;
using KnowledgeBase.Core.Observability;

namespace KnowledgeBase.Api.Infrastructure.Observability;

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        string? Header(string name) => OperationContext.Identifier(context.Request.Headers[name].FirstOrDefault());
        using var operation = OperationContext.Push(new(Header("X-Session-Id"), Header("X-Upload-Batch-Id"), Header("X-Upload-Id"), Header("X-Causation-Id")));
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestId"] = context.TraceIdentifier,
            ["IsEventStream"] = context.Request.Method == "GET" && string.Equals(context.Request.Path.Value, "/api/events", StringComparison.OrdinalIgnoreCase),
            ["ClientRequestId"] = Header("X-Request-Id"),
            ["Source"] = Header("X-Request-Source"),
            ["Trigger"] = Header("X-Request-Trigger"),
            ["AssetId"] = context.Request.RouteValues.TryGetValue("fileName", out var name)
                ? OperationContext.Identifier(Path.GetFileNameWithoutExtension(name?.ToString())) : null,
        });
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Trace-Id"] = Activity.Current?.TraceId.ToString();
            return Task.CompletedTask;
        });
        var started = Stopwatch.GetTimestamp();
        var outcome = "completed";
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        { outcome = "cancelled"; throw; }
        catch (Exception error)
        {
            outcome = "failed";
            logger.LogError(error, "Request failed with {ErrorType}", error.GetType().Name);
            throw;
        }
        finally
        {
            var endpoint = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            var status = outcome == "failed" ? 500 : context.Response.StatusCode;
            var level = status >= 500 ? LogLevel.Error : status >= 400 ? LogLevel.Warning : LogLevel.Information;
            if (context.Request.Path.StartsWithSegments("/api/diagnostics")) level = LogLevel.Debug;
            logger.Log(level, "HTTP {Method} {Route} {StatusCode} {Outcome} in {DurationMs} ms",
                context.Request.Method, endpoint, status, outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}
