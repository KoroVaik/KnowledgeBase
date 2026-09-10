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

/// <summary>The notes the pipeline produces from uploaded files.</summary>
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

    /// <summary>Lists notes, newest first, no body. <c>kind</c> filters to one kind.</summary>
    /// <response code="200">The listing, possibly empty.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<NoteSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken, [FromQuery] NoteKind? kind = null)
    {
        var query = _database.Notes.AsQueryable();

        if (kind is { } wanted)
        {
            query = query.Where(note => note.Kind == wanted);
        }

        var notes = await query
            .OrderByDescending(note => note.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(await SummariseAllAsync(notes, cancellationToken));
    }

    /// <summary>Lists the binned notes, most recently binned first.</summary>
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

        return Ok(await SummariseAllAsync(notes, cancellationToken));
    }

    /// <summary>Returns one note with its Markdown body.</summary>
    /// <response code="200">The note.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">No such note.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(NoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        // Binned notes stay readable by id - the bin must show what it holds.
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
            await LoadTagsAsync(note.Id, cancellationToken),
            note.Kind.ToString(),
            note.Body,
            await ResolveLinksAsync(note, cancellationToken),
            note.SourceAssetId,
            note.SourceFileName,
            note.CreatedAtUtc,
            note.UpdatedAtUtc,
            note.DeletedAtUtc));
    }

    /// <summary>Lists the live notes that link to this one. Used by the delete confirmation.</summary>
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
    /// Puts a note in the bin. <c>deleteSource</c> (default true) also deletes the file it was
    /// made from; when false, the file's job row goes instead so it shows as unprocessed.
    /// </summary>
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
                // Cascades to the job row; SourceFileName survives for the bin entry.
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

    /// <summary>Takes a note back out of the bin.</summary>
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
            // A re-run may already have produced a newer note for this file; the newer one wins.
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

    /// <summary>Deletes a binned note for good. Links to it become "no such note".</summary>
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
    /// Re-runs the pipeline over this note's file. The note stays until the new one is ready,
    /// then moves to the bin and the fresh one inherits its inbound links.
    /// </summary>
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

    // Link rows are asked first (they survive a re-run renaming the target); the title lookup
    // behind them covers body text that never became a row.
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

        // Binned targets are the whole point, so the query filter comes off.
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
                    // A live note wins over a binned one with the same title.
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

    private async Task<IReadOnlyList<NoteSummaryResponse>> SummariseAllAsync(
        IReadOnlyList<Note> notes,
        CancellationToken cancellationToken)
    {
        var tags = await LoadTagsAsync(notes.Select(note => note.Id).ToList(), cancellationToken);

        return notes
            .Select(note => new NoteSummaryResponse(
                note.Id,
                note.Title,
                tags.GetValueOrDefault(note.Id, []),
                note.Kind.ToString(),
                note.SourceAssetId,
                note.SourceFileName,
                note.CreatedAtUtc,
                note.UpdatedAtUtc,
                note.DeletedAtUtc))
            .ToList();
    }

    private async Task<IReadOnlyList<NoteTagResponse>> LoadTagsAsync(
        string noteId,
        CancellationToken cancellationToken)
    {
        var tags = await LoadTagsAsync([noteId], cancellationToken);
        return tags.GetValueOrDefault(noteId, []);
    }

    // NoteTag has no query filter, so a binned note keeps its tags on screen (Get / Trash).
    private async Task<Dictionary<string, IReadOnlyList<NoteTagResponse>>> LoadTagsAsync(
        IReadOnlyCollection<string> noteIds,
        CancellationToken cancellationToken)
    {
        if (noteIds.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<NoteTagResponse>>();
        }

        var rows = await (
            from noteTag in _database.NoteTags
            where noteIds.Contains(noteTag.NoteId)
            join tag in _database.Tags on noteTag.TagId equals tag.Id
            orderby noteTag.Ordinal
            select new { noteTag.NoteId, tag.Name, tag.Confirmed })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.NoteId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<NoteTagResponse>)group
                    .Select(row => new NoteTagResponse(row.Name, row.Confirmed))
                    .ToList());
    }
}
