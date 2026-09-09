using KnowledgeBase.Api.Controllers.Notes.Contracts;
using KnowledgeBase.Core.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Controllers.Notes;

/// <summary>
/// The notes the pipeline produces from uploaded files.
/// </summary>
/// <remarks>
/// Read-only for now: notes are written by the worker, not by hand. The body lives in a
/// Postgres column, not an .md file - the wiki-link graph and full-text search belong in the
/// database.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public sealed class NotesController(KnowledgeBaseDbContext database) : ControllerBase
{
    private readonly KnowledgeBaseDbContext _database = database;

    /// <summary>
    /// Lists every note, newest first. No body - the front polls this on load.
    /// </summary>
    /// <param name="cancellationToken">Cancels the listing if the client disconnects.</param>
    /// <response code="200">The listing, possibly empty.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<NoteSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var notes = await _database.Notes
            .OrderByDescending(note => note.UpdatedAtUtc)
            .Select(note => new NoteSummaryResponse(
                note.Id,
                note.Title,
                note.Category,
                note.CreatedAtUtc,
                note.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(notes);
    }

    /// <summary>
    /// Returns one note with its Markdown body.
    /// </summary>
    /// <param name="id">The note id, as returned by the listing.</param>
    /// <param name="cancellationToken">Cancels the lookup if the client disconnects.</param>
    /// <response code="200">The note.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">No such note.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(NoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var note = await _database.Notes
            .Where(note => note.Id == id)
            .Select(note => new NoteResponse(
                note.Id,
                note.Title,
                note.Category,
                note.Body,
                note.SourceAssetId,
                note.CreatedAtUtc,
                note.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return note is null ? NotFound() : Ok(note);
    }
}
