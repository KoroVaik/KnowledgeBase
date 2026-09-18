using System.Text.Json;
using System.Text.RegularExpressions;
using KnowledgeBase.Api.Controllers.Auth;
using KnowledgeBase.Core.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Controllers.Preferences;

/// <summary>
/// UI settings of the signed-in user (which sections are collapsed, and so on). Stored on the
/// server so they follow the user across browsers and devices.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public sealed partial class PreferencesController(KnowledgeBaseDbContext database) : ControllerBase
{
    private const int MaxValueLength = 4096;

    private readonly KnowledgeBaseDbContext _database = database;

    /// <summary>Returns every stored setting as one object, key → JSON value.</summary>
    /// <response code="200">The settings; an empty object when nothing was saved yet.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyDictionary<string, JsonElement>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();

        var rows = await _database.UserPreferences
            .Where(preference => preference.UserId == userId)
            .Select(preference => new { preference.Key, preference.ValueJson })
            .ToListAsync(cancellationToken);

        return Ok(rows.ToDictionary(row => row.Key, row => JsonSerializer.Deserialize<JsonElement>(row.ValueJson)));
    }

    /// <summary>Creates or replaces one setting. The body is the raw JSON value, e.g. <c>true</c>.</summary>
    /// <param name="key">Lower-case letters, digits and <c>: . _ -</c>, up to 100 characters.</param>
    /// <param name="value">Any JSON value up to 4 KB.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="204">Saved.</response>
    /// <response code="400">Malformed key, or the value is too large.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpPut("{key}")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Put(string key, [FromBody] JsonElement value, CancellationToken cancellationToken)
    {
        if (!KeyShape().IsMatch(key))
        {
            return BadRequest(new { error = "Key must be 1-100 characters of a-z, 0-9, ':', '.', '_' or '-'." });
        }

        var json = value.GetRawText();

        if (json.Length > MaxValueLength)
        {
            return BadRequest(new { error = $"Value must be at most {MaxValueLength} characters of JSON." });
        }

        var userId = User.GetUserId();
        var now = DateTime.UtcNow;

        // One upsert statement: two quick toggles of the same key would race a read-then-write.
        await _database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "UserPreferences" ("UserId", "Key", "ValueJson", "UpdatedAtUtc")
            VALUES ({userId}, {key}, {json}::jsonb, {now})
            ON CONFLICT ("UserId", "Key")
            DO UPDATE SET "ValueJson" = EXCLUDED."ValueJson", "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
            """,
            cancellationToken);

        return NoContent();
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9:._-]{0,99}$")]
    private static partial Regex KeyShape();
}
