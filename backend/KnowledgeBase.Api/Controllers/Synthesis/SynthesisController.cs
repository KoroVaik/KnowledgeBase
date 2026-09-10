using KnowledgeBase.Api.Controllers.Synthesis.Contracts;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline.Synthesis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Controllers.Synthesis;

/// <summary>Merges existing notes into aggregate (Synthesis / Index) notes via the worker.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public sealed class SynthesisController(KnowledgeBaseDbContext database) : ControllerBase
{
    private const int MinNotesPerGroup = 2;

    // The one Index note's group key - there is only ever one.
    private const string IndexGroup = "index";

    private readonly KnowledgeBaseDbContext _database = database;

    /// <summary>
    /// Queues a job that merges every live Source note carrying <c>tag</c> into one Synthesis
    /// note. A later call for the same tag replaces that note.
    /// </summary>
    /// <response code="202">Queued.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">No such tag.</response>
    /// <response code="409">Fewer than two notes carry the tag, or a run is already queued.</response>
    [HttpPost("tag")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SynthesiseTag(
        [FromBody] SynthesiseTagRequest request,
        CancellationToken cancellationToken)
    {
        var tag = await _database.Tags
            .FirstOrDefaultAsync(existing => existing.Name == request.Tag, cancellationToken);

        if (tag is null)
        {
            return NotFound(new { error = "No such tag." });
        }

        var noteIds = await (
            from noteTag in _database.NoteTags
            join note in _database.Notes on noteTag.NoteId equals note.Id
            where noteTag.TagId == tag.Id && note.Kind == NoteKind.Source
            select note.Id)
            .ToListAsync(cancellationToken);

        if (noteIds.Count < MinNotesPerGroup)
        {
            return Conflict(new { error = $"Fewer than {MinNotesPerGroup} notes carry the tag “{tag.Name}”." });
        }

        var queued = await SynthesisQueue.EnqueueAsync(
            _database, NoteKind.Synthesis, tag.Name, noteIds, cancellationToken);

        if (!queued)
        {
            return Conflict(new { error = "A synthesis of this tag is already queued." });
        }

        await _database.SaveChangesAsync(CancellationToken.None);

        return Accepted();
    }

    /// <summary>
    /// Queues a job that merges every Synthesis note into the single Index note - a table of
    /// contents for the knowledge base. A later call replaces it.
    /// </summary>
    /// <response code="202">Queued.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="409">Fewer than two Synthesis notes exist, or a run is already queued.</response>
    [HttpPost("index")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SynthesiseIndex(CancellationToken cancellationToken)
    {
        var noteIds = await _database.Notes
            .Where(note => note.Kind == NoteKind.Synthesis)
            .Select(note => note.Id)
            .ToListAsync(cancellationToken);

        if (noteIds.Count < MinNotesPerGroup)
        {
            return Conflict(new { error = $"Fewer than {MinNotesPerGroup} synthesis notes to index." });
        }

        var queued = await SynthesisQueue.EnqueueAsync(
            _database, NoteKind.Index, IndexGroup, noteIds, cancellationToken);

        if (!queued)
        {
            return Conflict(new { error = "An index rebuild is already queued." });
        }

        await _database.SaveChangesAsync(CancellationToken.None);

        return Accepted();
    }
}
