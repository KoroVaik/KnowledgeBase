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

/// <summary>The live change stream every open tab listens to.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class EventsController(IChangeNotifier notifier, IOptions<EventsOptions> options) : ControllerBase
{
    private readonly EventsOptions _options = options.Value;

    // Under a minute, or a proxy / mobile network starts dropping the silent connection.
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);

    private readonly IChangeNotifier _notifier = notifier;

    /// <summary>
    /// Streams change events as Server-Sent Events: one GET whose response never ends, a
    /// <c>data: {json}</c> block per change. Browser side is <c>EventSource</c>.
    /// </summary>
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

        // nginx-style proxies hold a response back until it ends - never, for this one.
        Response.Headers["X-Accel-Buffering"] = "no";

        // Or Kestrel buffers each event until the buffer fills.
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var subscription = _notifier.Subscribe();

        // Flush headers now so the browser reports the stream open before the first change.
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
            // Tab closed or connection died - the normal way out. A write to a dead socket
            // lands here too, cleaning up a client that vanished silently.
        }
    }

    /// <summary>
    /// Accepts a change hint from the worker (a separate process, no access to the in-memory
    /// notifier) and fans it out to every open stream. Guarded by a shared token, not a session.
    /// </summary>
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
        // Cancel the wait, don't race a timer: an abandoned WaitToReadAsync stacks readers.
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
            // Quiet interval: a comment line keeps the connection alive and fails if the
            // client is already gone. The browser drops it silently.
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
