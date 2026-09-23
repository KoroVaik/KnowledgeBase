using System.Diagnostics;

namespace KnowledgeBase.Core.Observability;

public sealed class DependencyLoggingHandler(ILogger<DependencyLoggingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            logger.Log(response.IsSuccessStatusCode ? LogLevel.Debug : LogLevel.Warning,
                "Dependency {Method} {Host} {Path} returned {StatusCode} in {DurationMs} ms",
                request.Method.Method, request.RequestUri?.Host, request.RequestUri?.AbsolutePath,
                (int)response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return response;
        }
        catch (Exception error)
        {
            logger.Log(cancellationToken.IsCancellationRequested ? LogLevel.Information : LogLevel.Error, error,
                "Dependency {Host} failed after {DurationMs} ms", request.RequestUri?.Host, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}
