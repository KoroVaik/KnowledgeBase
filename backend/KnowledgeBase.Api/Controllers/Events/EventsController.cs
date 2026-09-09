using System.Text.Json;
using KnowledgeBase.Core.RealTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeBase.Api.Controllers.Events;

/// <summary>
/// The live change stream every open tab listens to.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class EventsController(IChangeNotifier notifier) : ControllerBase
{
    // Long enough to stay cheap, short enough to beat the idle timeout of a proxy or a mobile
    // network - those start dropping a silent connection at about a minute.
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(20);

    // Web defaults, so the payload is camelCase like every other response here.
    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);

    private readonly IChangeNotifier _notifier = notifier;

    /// <summary>
    /// Streams change events until the caller goes away (Server-Sent Events).
    /// </summary>
    /// <param name="cancellationToken">Fires when the browser closes the tab or the connection drops.</param>
    /// <remarks>
    /// One ordinary GET whose response never ends: the body keeps growing, one
    /// <c>data: {json}</c> block per change. The browser side is <c>EventSource</c>.
    /// Events carry no payload beyond what changed - see <see cref="ChangeEvent"/>.
    /// </remarks>
    /// <response code="200">The stream is open.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task Stream(CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        // nginx-style proxies hold a response back until it ends, which for this one is never.
        Response.Headers["X-Accel-Buffering"] = "no";

        // Kestrel would otherwise keep each event in its write buffer until the buffer fills.
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var subscription = _notifier.Subscribe();

        // Pushes the headers out now, so the browser reports the stream as open instead of
        // waiting for the first change - which may be hours away.
        await Response.Body.FlushAsync(cancellationToken);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!await WriteNextAsync(subscription, cancellationToken))
                {
                    break;
                }

                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The tab was closed or the connection died - the only normal way out of this loop.
            // A write to a socket that is already gone lands here too, which is how a client
            // that vanished without saying goodbye gets cleaned up.
        }
    }

    private async Task<bool> WriteNextAsync(ChangeSubscription subscription, CancellationToken cancellationToken)
    {
        // Cancelling the wait rather than racing it against a timer: an abandoned
        // WaitToReadAsync would pile up a second reader on every quiet interval.
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idle.CancelAfter(KeepAliveInterval);

        try
        {
            if (!await subscription.Reader.WaitToReadAsync(idle.Token))
            {
                return false;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Nothing changed for a while. A comment line - the browser drops it silently -
            // keeps the connection off the idle-timeout list, and fails right here if the
            // client is already gone.
            await Response.WriteAsync(": ping\n\n", cancellationToken);

            return true;
        }

        while (subscription.Reader.TryRead(out var change))
        {
            await Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(change, EventJson)}\n\n",
                cancellationToken);
        }

        return true;
    }
}
