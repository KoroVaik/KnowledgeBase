using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgeBase.Api.Controllers.Events.Configuration;
using KnowledgeBase.Core.RealTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Api.Controllers.Events;

/// <summary>
/// The live change stream every open tab listens to.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class EventsController(IChangeNotifier notifier, IOptions<EventsOptions> options) : ControllerBase
{
    private readonly EventsOptions _options = options.Value;

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

    /// <summary>
    /// Accepts a change hint from the worker and fans it out to every open stream (Server-Sent Events).
    /// </summary>
    /// <remarks>
    /// The worker runs in its own process and cannot reach the in-memory notifier directly, so it
    /// posts here instead. Guarded by a shared token, not a session: there is no user behind this
    /// call. A lost hint is harmless - the browser re-reads on its next reconnect either way.
    /// </remarks>
    /// <response code="204">The hint was fanned out.</response>
    /// <response code="401">The token is missing or wrong.</response>
    /// <response code="503">No ingest token is configured, so the endpoint is closed.</response>
    [HttpPost("ingest")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult Ingest(
        [FromBody] ChangeEvent change,
        [FromHeader(Name = "X-Ingest-Token")] string? token)
    {
        if (string.IsNullOrEmpty(_options.IngestToken))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (token is null || !TokenMatches(token))
        {
            return Unauthorized();
        }

        _notifier.Publish(change);

        return NoContent();
    }

    private bool TokenMatches(string presented) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented),
            Encoding.UTF8.GetBytes(_options.IngestToken));

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
