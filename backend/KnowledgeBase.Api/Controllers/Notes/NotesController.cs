using KnowledgeBase.Api.Controllers.Notes.Contracts;
using KnowledgeBase.Core.Notes;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.RealTime;
using KnowledgeBase.Core.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Controllers.Notes;

/// <summary>
/// The notes the pipeline produces from uploaded files.
/// </summary>
/// <remarks>
/// Bodies are written by the worker, not by hand, and live in a Postgres column rather than an
/// .md file - the wiki-link graph and full-text search belong in the database.
///
/// Deleting is soft: the row stays with DeletedAtUtc set. A link pointing at a binned note can
/// then be told apart from a link to a note that never existed, which is the difference between
/// "deleted" and "not created yet" on screen.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public sealed class NotesController(
    KnowledgeBaseDbContext database,
    IAssetStorage storage,
    IChangeNotifier notifier) : ControllerBase
{
    private readonly KnowledgeBaseDbContext _database = database;
    private readonly IAssetStorage _storage = storage;
    private readonly IChangeNotifier _notifier = notifier;

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
            .ToListAsync(cancellationToken);

        return Ok(notes.Select(Summarise).ToList());
    }

    /// <summary>
    /// Lists the binned notes, most recently binned first.
    /// </summary>
    /// <param name="cancellationToken">Cancels the listing if the client disconnects.</param>
    /// <response code="200">The listing, possibly empty.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet("trash")]
    [ProducesResponseType(typeof(IReadOnlyList<NoteSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Trash(CancellationToken cancellationToken)
    {
        var notes = await _database.Notes
            .IgnoreQueryFilters()
            .Where(note => note.DeletedAtUtc != null)
            .OrderByDescending(note => note.DeletedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(notes.Select(Summarise).ToList());
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
        // Binned notes stay readable by id: the bin has to show what it is holding, and a link
        // pointing at one still wants its title.
        var note = await _database.Notes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(note => note.Id == id, cancellationToken);

        if (note is null)
        {
            return NotFound();
        }

        return Ok(new NoteResponse(
            note.Id,
            note.Title,
            note.Category,
            note.Kind.ToString(),
            note.Body,
            await ResolveLinksAsync(note, cancellationToken),
            note.SourceAssetId,
            note.SourceFileName,
            note.CreatedAtUtc,
            note.UpdatedAtUtc,
            note.DeletedAtUtc));
    }

    /// <summary>
    /// Lists the live notes that link to this one.
    /// </summary>
    /// <param name="id">The note being pointed at.</param>
    /// <param name="cancellationToken">Cancels the lookup if the client disconnects.</param>
    /// <remarks>
    /// Asked for when the delete confirmation opens, so it can name what goes grey instead of
    /// just asking whether the user is sure.
    /// </remarks>
    /// <response code="200">The listing, possibly empty.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet("{id}/backlinks")]
    [ProducesResponseType(typeof(IReadOnlyList<BacklinkResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Backlinks(string id, CancellationToken cancellationToken)
    {
        var sources = _database.NoteLinks
            .Where(link => link.TargetNoteId == id)
            .Select(link => link.SourceNoteId);

        var notes = await _database.Notes
            .Where(note => sources.Contains(note.Id))
            .OrderBy(note => note.Title)
            .ToListAsync(cancellationToken);

        return Ok(notes
            .Select(note => new BacklinkResponse(note.Id, note.Title, note.Kind.ToString()))
            .ToList());
    }

    /// <summary>
    /// Puts a note in the bin, and by default its source file with it.
    /// </summary>
    /// <param name="id">The note id, as returned by the listing.</param>
    /// <param name="cancellationToken">Cancels the call if the client disconnects.</param>
    /// <param name="deleteSource">Delete the file the note was made from. On by default.</param>
    /// <remarks>
    /// A source note is a structured description of one file, so the file goes with it unless
    /// the caller says otherwise. When the file stays, its job row goes instead: that row is the
    /// record of having produced the note that is now gone, and without it the file shows up as
    /// unprocessed and can be run through the pipeline again.
    /// </remarks>
    /// <response code="204">The note is in the bin.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">No such note, or it is in the bin already.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        string id,
        CancellationToken cancellationToken,
        [FromQuery] bool deleteSource = true)
    {
        var note = await _database.Notes.FirstOrDefaultAsync(note => note.Id == id, cancellationToken);

        if (note is null)
        {
            return NotFound();
        }

        note.DeletedAtUtc = DateTime.UtcNow;

        AssetRecord? asset = null;

        if (note.SourceAssetId is { } assetId)
        {
            asset = await _database.Assets.FirstOrDefaultAsync(record => record.Id == assetId, cancellationToken);

            if (asset is not null && deleteSource)
            {
                // Takes the job row with it and clears this note's pointer at the file.
                // SourceFileName survives, so the entry in the bin can still name it.
                _database.Assets.Remove(asset);
            }
            else if (asset is not null)
            {
                var job = await _database.ProcessingJobs
                    .FirstOrDefaultAsync(job => job.AssetId == assetId, cancellationToken);

                if (job is not null)
                {
                    _database.ProcessingJobs.Remove(job);
                }
            }
        }

        await _database.SaveChangesAsync(CancellationToken.None);

        if (asset is not null && deleteSource)
        {
            await _storage.DeleteAsync(asset.StoredFileName, cancellationToken);
        }

        _notifier.Publish(new ChangeEvent(ChangeResources.Notes, ChangeActions.Deleted, id));

        if (asset is not null)
        {
            _notifier.Publish(new ChangeEvent(
                ChangeResources.Assets,
                deleteSource ? ChangeActions.Deleted : ChangeActions.Updated,
                asset.StoredFileName));
        }

        return NoContent();
    }

    /// <summary>
    /// Takes a note back out of the bin.
    /// </summary>
    /// <param name="id">The binned note.</param>
    /// <param name="cancellationToken">Cancels the call if the client disconnects.</param>
    /// <remarks>
    /// Links that pointed here start working again on their own - they still hold the id.
    /// </remarks>
    /// <response code="204">The note is back.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">No such note, or it was never binned.</response>
    /// <response code="409">Its file is gone, the file already has a current note, or the title is taken.</response>
    [HttpPost("{id}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(string id, CancellationToken cancellationToken)
    {
        var note = await _database.Notes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(note => note.Id == id, cancellationToken);

        if (note is null || note.DeletedAtUtc is null)
        {
            return NotFound();
        }

        if (note.Kind == NoteKind.Source && note.SourceAssetId is null)
        {
            return Conflict(new { error = "The file this note describes is gone, so the note cannot come back." });
        }

        if (note.SourceAssetId is { } assetId)
        {
            // A re-run may have produced a newer note for the same file. Choosing which version
            // is the live one is a feature of its own; until it exists, the newer one wins.
            var current = await _database.Notes
                .AnyAsync(other => other.SourceAssetId == assetId, cancellationToken);

            if (current)
            {
                return Conflict(new { error = "This file already has a current note. Delete that one first." });
            }
        }

        var titleTaken = await _database.Notes
            .AnyAsync(other => other.Title == note.Title, cancellationToken);

        if (titleTaken)
        {
            return Conflict(new { error = "A note with this title already exists." });
        }

        note.DeletedAtUtc = null;

        await _database.SaveChangesAsync(CancellationToken.None);

        _notifier.Publish(new ChangeEvent(ChangeResources.Notes, ChangeActions.Created, id));

        return NoContent();
    }

    /// <summary>
    /// Deletes a binned note for good.
    /// </summary>
    /// <param name="id">The binned note.</param>
    /// <param name="cancellationToken">Cancels the call if the client disconnects.</param>
    /// <remarks>
    /// Links pointing here stop saying "deleted" and start saying "no such note": once the row
    /// is gone, that is genuinely all anyone knows.
    /// </remarks>
    /// <response code="204">The row is gone.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">No such note, or it is not in the bin.</response>
    [HttpDelete("{id}/purge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Purge(string id, CancellationToken cancellationToken)
    {
        var note = await _database.Notes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(note => note.Id == id, cancellationToken);

        if (note is null || note.DeletedAtUtc is null)
        {
            return NotFound();
        }

        _database.Notes.Remove(note);

        await _database.SaveChangesAsync(CancellationToken.None);

        _notifier.Publish(new ChangeEvent(ChangeResources.Notes, ChangeActions.Deleted, id));

        return NoContent();
    }

    /// <summary>
    /// Runs the pipeline over this note's file again.
    /// </summary>
    /// <param name="id">The note to redo.</param>
    /// <param name="cancellationToken">Cancels the call if the client disconnects.</param>
    /// <remarks>
    /// The note stays where it is until the new one is ready; then it moves to the bin and the
    /// fresh one takes its place, inheriting the links that pointed at it.
    /// </remarks>
    /// <response code="202">Queued.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">No such note.</response>
    /// <response code="409">The note has no file, or a run is already queued.</response>
    [HttpPost("{id}/process-again")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ProcessAgain(string id, CancellationToken cancellationToken)
    {
        var note = await _database.Notes.FirstOrDefaultAsync(note => note.Id == id, cancellationToken);

        if (note is null)
        {
            return NotFound();
        }

        if (note.SourceAssetId is not { } assetId)
        {
            return Conflict(new { error = "This note was not made from a file." });
        }

        var asset = await _database.Assets.FirstOrDefaultAsync(record => record.Id == assetId, cancellationToken);

        if (asset is null)
        {
            return Conflict(new { error = "The file this note was made from is gone." });
        }

        if (!await ProcessingQueue.EnsurePendingAsync(_database, assetId, cancellationToken))
        {
            return Conflict(new { error = "This file is already queued." });
        }

        await _database.SaveChangesAsync(CancellationToken.None);

        _notifier.Publish(new ChangeEvent(ChangeResources.Assets, ChangeActions.Updated, asset.StoredFileName));

        return Accepted();
    }

    /// <summary>
    /// Works out what each [[title]] in the body points at, for the reader to render.
    /// </summary>
    /// <remarks>
    /// The link rows are asked first: they say where the link actually goes, which survives the
    /// target being renamed by a re-run. The title lookup behind them covers text in the body
    /// that never became a row - a link the model wrote inline, or one to a note yet to exist.
    /// </remarks>
    private async Task<IReadOnlyList<NoteLinkStateResponse>> ResolveLinksAsync(
        Note note,
        CancellationToken cancellationToken)
    {
        var titles = WikiLink.TitlesIn(note.Body);

        if (titles.Count == 0)
        {
            return [];
        }

        var edges = await _database.NoteLinks
            .Where(link => link.SourceNoteId == note.Id)
            .Select(link => new { link.TargetTitle, link.TargetNoteId })
            .ToListAsync(cancellationToken);

        var pointsAt = edges
            .Where(edge => edge.TargetNoteId != null)
            .ToDictionary(edge => edge.TargetTitle, edge => edge.TargetNoteId!, StringComparer.OrdinalIgnoreCase);

        var wanted = pointsAt.Values.ToList();

        // Binned targets are the whole point of asking, so the filter comes off here.
        var candidates = await _database.Notes
            .IgnoreQueryFilters()
            .Where(other => wanted.Contains(other.Id) || titles.Contains(other.Title))
            .Select(other => new
            {
                other.Id,
                other.Title,
                Binned = other.DeletedAtUtc != null,
            })
            .ToListAsync(cancellationToken);

        var byId = candidates.ToDictionary(candidate => candidate.Id);

        return titles
            .Select(title =>
            {
                if (pointsAt.TryGetValue(title, out var targetId) && byId.TryGetValue(targetId, out var target))
                {
                    return new NoteLinkStateResponse(
                        title,
                        target.Binned ? NoteLinkStates.Deleted : NoteLinkStates.Resolved,
                        target.Id);
                }

                var named = candidates
                    .Where(candidate => string.Equals(candidate.Title, title, StringComparison.OrdinalIgnoreCase))
                    // A live note wins over a binned one with the same title: the bin is not
                    // holding this name any more, something else is.
                    .OrderBy(candidate => candidate.Binned)
                    .FirstOrDefault();

                if (named is null)
                {
                    return new NoteLinkStateResponse(title, NoteLinkStates.Missing, null);
                }

                return new NoteLinkStateResponse(
                    title,
                    named.Binned ? NoteLinkStates.Deleted : NoteLinkStates.Resolved,
                    named.Id);
            })
            .ToList();
    }

    private static NoteSummaryResponse Summarise(Note note) => new(
        note.Id,
        note.Title,
        note.Category,
        note.Kind.ToString(),
        note.SourceAssetId,
        note.SourceFileName,
        note.CreatedAtUtc,
        note.UpdatedAtUtc,
        note.DeletedAtUtc);
}
