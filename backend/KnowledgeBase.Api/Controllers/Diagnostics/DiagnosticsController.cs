using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using KnowledgeBase.Core.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KnowledgeBase.Api.Controllers.Diagnostics;

/// <summary>Accepts bounded, authenticated browser diagnostics batches.</summary>
[ApiController]
[Authorize]
[Route("api/diagnostics")]
public sealed class DiagnosticsController(ILogger<DiagnosticsController> logger) : ControllerBase
{
    private static readonly HashSet<string> AllowedFields = new(StringComparer.Ordinal)
    {
        "source", "trigger", "requestId", "traceId", "spanId", "uploadBatchId", "uploadId", "assetId", "causationId",
        "method", "route", "status", "durationMs", "outcome", "count", "inFlight", "errorType", "message", "stack",
        "bytes", "resource", "action", "delayMs", "dropped", "sequence", "release", "eventId", "jobId", "maxDurationMs", "completed", "failed",
    };

    /// <summary>Records up to 50 events; field names and payload sizes are restricted.</summary>
    [HttpPost("events")]
    [EnableRateLimiting("diagnostics")]
    [RequestSizeLimit(65536)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult Record([FromBody] DiagnosticBatch batch)
    {
        if (OperationContext.Identifier(batch.SessionId) is null || batch.Events.Count is < 1 or > 50) return BadRequest();
        foreach (var item in batch.Events)
        {
            if (item is null || OperationContext.Identifier(item.Event) is null || item.Fields.Count > 24
                || item.Fields.Any(p => !AllowedFields.Contains(p.Key) || p.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)
                    || p.Value.ValueKind == JsonValueKind.String && (p.Value.GetString()?.Length ?? 0) > 2048))
                return BadRequest();
        }
        using var session = logger.BeginScope(new Dictionary<string, object?> { ["SessionId"] = batch.SessionId, ["Origin"] = "browser", ["Service"] = "frontend" });
        foreach (var item in batch.Events)
        {
            var fields = item.Fields.ToDictionary(p => char.ToUpperInvariant(p.Key[0]) + p.Key[1..], p => ConvertValue(p.Value));
            fields["ClientTimestamp"] = item.Timestamp;
            using var scope = logger.BeginScope(fields);
            logger.Log(item.Level switch { "error" => LogLevel.Error, "warning" => LogLevel.Warning, "debug" => LogLevel.Debug, _ => LogLevel.Information },
                "Browser {Event}", item.Event);
        }
        return NoContent();
    }

    private static object? ConvertValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => LogRedaction.Text(value.GetString()!),
        JsonValueKind.Number => value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}

public sealed record DiagnosticBatch([Required] string SessionId, [Required] List<BrowserDiagnostic> Events);
public sealed record BrowserDiagnostic([Required] string Event, DateTimeOffset Timestamp, [Required] string Level, [Required] Dictionary<string, JsonElement> Fields);
