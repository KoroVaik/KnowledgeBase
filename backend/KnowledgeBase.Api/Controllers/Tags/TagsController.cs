using KnowledgeBase.Api.Controllers.Tags.Contracts;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline.TagGrouping;
using KnowledgeBase.Core.RealTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Controllers.Tags;

/// <summary>The tag vocabulary: browse, review pipeline-invented tags, merge, delete.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public sealed class TagsController(KnowledgeBaseDbContext database, IChangeNotifier notifier) : ControllerBase
{
    private readonly KnowledgeBaseDbContext _database = database;
    private readonly IChangeNotifier _notifier = notifier;

    /// <summary>
    /// Lists tags with their live Source-note count. With no <c>query</c>, busiest first - the
    /// vocabulary browser. With one, only tags matching it, closest match first - a merge-target
    /// search meant to stay usable past a hundred tags. <c>excludeId</c> drops one tag (typically
    /// the one being merged) from the results.
    /// </summary>
    /// <response code="200">The listing, possibly empty.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TagResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(
        CancellationToken cancellationToken,
        [FromQuery] string? query = null,
        [FromQuery] string? excludeId = null)
    {
        // Count only Source notes: a synthesis already carries the tag it was built for, and
        // counting it would inflate the number the "synthesise" button is gated on.
        var counts = await (
            from noteTag in _database.NoteTags
            join note in _database.Notes on noteTag.NoteId equals note.Id
            where note.Kind == NoteKind.Source
            group noteTag by noteTag.TagId into byTag
            select new { TagId = byTag.Key, Count = byTag.Count() })
            .ToDictionaryAsync(row => row.TagId, row => row.Count, cancellationToken);

        var parentIds = await _database.TagParents.ToListAsync(cancellationToken);
        var parentsByChild = parentIds
            .GroupBy(link => link.ChildId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(link => link.ParentId).ToList());

        var tags = await _database.Tags.ToListAsync(cancellationToken);

        IEnumerable<Tag> matching = excludeId is null ? tags : tags.Where(tag => tag.Id != excludeId);

        var term = query?.Trim();

        // Plain text search, not the model's semantic call (see Tag review in docs/database.md) -
        // this ranks a human's typing for a pick list, it does not decide a "correct" merge.
        var ranked = string.IsNullOrEmpty(term)
            ? matching.Select(tag => (Tag: tag, Rank: 0))
            : matching
                .Select(tag => (Tag: tag, Rank: MatchRank(tag.Name, term)))
                .Where(row => row.Rank < NoMatch);

        var response = ranked
            .OrderBy(row => row.Rank)
            .ThenByDescending(row => counts.GetValueOrDefault(row.Tag.Id))
            .ThenBy(row => row.Tag.Name)
            .Select(row => new TagResponse(
                row.Tag.Id,
                row.Tag.Name,
                row.Tag.Confirmed,
                counts.GetValueOrDefault(row.Tag.Id),
                row.Tag.SuggestedMergeIntoId,
                parentsByChild.GetValueOrDefault(row.Tag.Id, Array.Empty<string>())))
            .ToList();

        return Ok(response);
    }

    private const int NoMatch = 3;

    private static int MatchRank(string name, string term) =>
        string.Equals(name, term, StringComparison.OrdinalIgnoreCase) ? 0
        : name.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 1
        : name.Contains(term, StringComparison.OrdinalIgnoreCase) ? 2
        : NoMatch;

    /// <summary>
    /// Adds a tag by hand - the picker's "Add new tag" escape hatch, for when neither the
    /// suggested nor the spelling-matched tags are the right one. Lands confirmed: a human
    /// typed it, so it needs no review.
    /// </summary>
    /// <response code="201">Created.</response>
    /// <response code="400">The name is blank.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="409">A tag with that name already exists.</response>
    [HttpPost]
    [ProducesResponseType(typeof(TagResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateTagRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        if (name.Length == 0)
        {
            return BadRequest(new { error = "The tag needs a name." });
        }

        var exists = await _database.Tags.AnyAsync(
            tag => tag.Name.ToLower() == name.ToLower(), cancellationToken);

        if (exists)
        {
            return Conflict(new { error = $"A tag named “{name}” already exists." });
        }

        var tag = new Tag
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Confirmed = true,
            SuggestedMergeIntoId = null,
        };

        _database.Tags.Add(tag);
        await _database.SaveChangesAsync(CancellationToken.None);

        _notifier.Publish(new ChangeEvent(ChangeResources.Notes, ChangeActions.Updated));

        return CreatedAtAction(nameof(List), new TagResponse(tag.Id, tag.Name, tag.Confirmed, 0, null, Array.Empty<string>()));
    }

    /// <summary>Marks a tag as vouched for by the user. Its merge suggestion is dropped.</summary>
    /// <response code="204">Confirmed.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">No such tag.</response>
    [HttpPost("{id}/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Confirm(string id, CancellationToken cancellationToken)
    {
        var tag = await _database.Tags.FirstOrDefaultAsync(tag => tag.Id == id, cancellationToken);

        if (tag is null)
        {
            return NotFound();
        }

        tag.Confirmed = true;
        tag.SuggestedMergeIntoId = null;

        await _database.SaveChangesAsync(CancellationToken.None);

        _notifier.Publish(new ChangeEvent(ChangeResources.Notes, ChangeActions.Updated));

        return NoContent();
    }

    /// <summary>
    /// Merges tag <c>id</c> into <c>intoId</c>: every note carrying the first now carries the
    /// second, the first is deleted and its synthesis note goes to the bin. Merging into a tag
    /// confirms it.
    /// </summary>
    /// <response code="204">Merged.</response>
    /// <response code="400">A tag cannot be merged into itself.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">Either tag does not exist.</response>
    [HttpPost("{id}/merge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Merge(
        string id,
        [FromBody] MergeTagRequest request,
        CancellationToken cancellationToken)
    {
        if (id == request.IntoId)
        {
            return BadRequest(new { error = "A tag cannot be merged into itself." });
        }

        var from = await _database.Tags.FirstOrDefaultAsync(tag => tag.Id == id, cancellationToken);
        var into = await _database.Tags.FirstOrDefaultAsync(tag => tag.Id == request.IntoId, cancellationToken);

        if (from is null || into is null)
        {
            return NotFound();
        }

        var moving = await _database.NoteTags
            .Where(link => link.TagId == from.Id)
            .ToListAsync(cancellationToken);

        var noteIds = moving.Select(link => link.NoteId).ToList();

        var alreadyThere = await _database.NoteTags
            .Where(link => link.TagId == into.Id && noteIds.Contains(link.NoteId))
            .ToDictionaryAsync(link => link.NoteId, cancellationToken);

        // The key includes TagId, so a link is re-added rather than repointed. A note that
        // carried both keeps the better (lower) position of the two.
        foreach (var link in moving)
        {
            if (alreadyThere.TryGetValue(link.NoteId, out var existing))
            {
                existing.Ordinal = Math.Min(existing.Ordinal, link.Ordinal);
            }
            else
            {
                _database.NoteTags.Add(new NoteTag { NoteId = link.NoteId, TagId = into.Id, Ordinal = link.Ordinal });
            }
        }

        var pointingAtFrom = await _database.Tags
            .Where(tag => tag.SuggestedMergeIntoId == from.Id)
            .ToListAsync(cancellationToken);

        foreach (var tag in pointingAtFrom)
        {
            tag.SuggestedMergeIntoId = into.Id;
        }

        into.Confirmed = true;
        into.SuggestedMergeIntoId = null;

        await BinSynthesisOfAsync(from, cancellationToken);
        _database.Tags.Remove(from);

        await _database.SaveChangesAsync(CancellationToken.None);

        _notifier.Publish(new ChangeEvent(ChangeResources.Notes, ChangeActions.Updated));

        return NoContent();
    }

    /// <summary>
    /// Adds a parent (is-a) tag: <c>id</c> becomes a child of <c>parentId</c>. Rejected if it
    /// would create a cycle - a tag may have several parents (a DAG), never a loop back to
    /// itself through them.
    /// </summary>
    /// <response code="204">Added.</response>
    /// <response code="400">A tag cannot be its own parent.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">Either tag does not exist.</response>
    /// <response code="409">Already a parent, or the parent is already a descendant of the child.</response>
    [HttpPost("{id}/parents")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddParent(
        string id,
        [FromBody] AddTagParentRequest request,
        CancellationToken cancellationToken)
    {
        if (id == request.ParentId)
        {
            return BadRequest(new { error = "A tag cannot be its own parent." });
        }

        var childExists = await _database.Tags.AnyAsync(tag => tag.Id == id, cancellationToken);
        var parentExists = await _database.Tags.AnyAsync(tag => tag.Id == request.ParentId, cancellationToken);

        if (!childExists || !parentExists)
        {
            return NotFound();
        }

        var alreadyLinked = await _database.TagParents.AnyAsync(
            link => link.ChildId == id && link.ParentId == request.ParentId, cancellationToken);

        if (alreadyLinked)
        {
            return Conflict(new { error = "Already a parent of this tag." });
        }

        if (await CreatesCycleAsync(id, request.ParentId, cancellationToken))
        {
            return Conflict(new { error = "That would make the tag its own ancestor." });
        }

        _database.TagParents.Add(new TagParent { ChildId = id, ParentId = request.ParentId });
        await _database.SaveChangesAsync(CancellationToken.None);

        _notifier.Publish(new ChangeEvent(ChangeResources.Notes, ChangeActions.Updated));

        return NoContent();
    }

    /// <summary>Adding child -&gt; parent would cycle if parent can already reach child by
    /// walking its own existing parents (parent -&gt; ... -&gt; child already exists).</summary>
    private async Task<bool> CreatesCycleAsync(string childId, string parentId, CancellationToken cancellationToken)
    {
        var edges = await _database.TagParents.ToListAsync(cancellationToken);
        var parentsOf = edges
            .GroupBy(link => link.ChildId)
            .ToDictionary(group => group.Key, group => group.Select(link => link.ParentId).ToList());

        var visited = new HashSet<string>();
        var toVisit = new Stack<string>();
        toVisit.Push(parentId);

        while (toVisit.TryPop(out var current))
        {
            if (current == childId)
            {
                return true;
            }

            if (!visited.Add(current) || !parentsOf.TryGetValue(current, out var itsParents))
            {
                continue;
            }

            foreach (var next in itsParents)
            {
                toVisit.Push(next);
            }
        }

        return false;
    }

    /// <summary>Removes a parent (is-a) link between two tags.</summary>
    /// <response code="204">Removed.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">No such link.</response>
    [HttpDelete("{id}/parents/{parentId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveParent(string id, string parentId, CancellationToken cancellationToken)
    {
        var link = await _database.TagParents.FirstOrDefaultAsync(
            link => link.ChildId == id && link.ParentId == parentId, cancellationToken);

        if (link is null)
        {
            return NotFound();
        }

        _database.TagParents.Remove(link);
        await _database.SaveChangesAsync(CancellationToken.None);

        _notifier.Publish(new ChangeEvent(ChangeResources.Notes, ChangeActions.Updated));

        return NoContent();
    }

    /// <summary>
    /// Queues a job that re-runs the closest-confirmed-tag suggestion over every unconfirmed
    /// tag, not only ones invented in the same run as a source note - useful once a tag gets
    /// confirmed after others that could have matched it already exist.
    /// </summary>
    /// <response code="202">Queued.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="409">Nothing to group, or a run is already queued.</response>
    [HttpPost("suggest-merges")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SuggestMerges(CancellationToken cancellationToken)
    {
        var hasConfirmed = await _database.Tags.AnyAsync(tag => tag.Confirmed, cancellationToken);
        var hasUnconfirmed = await _database.Tags.AnyAsync(tag => !tag.Confirmed, cancellationToken);

        if (!hasConfirmed || !hasUnconfirmed)
        {
            return Conflict(new { error = "Needs at least one confirmed and one unreviewed tag." });
        }

        var queued = await TagGroupingQueue.EnqueueAsync(_database, cancellationToken);

        if (!queued)
        {
            return Conflict(new { error = "A grouping run is already queued." });
        }

        await _database.SaveChangesAsync(CancellationToken.None);

        return Accepted();
    }

    /// <summary>
    /// Deletes a tag: it comes off every note, and its synthesis note goes to the bin. A note
    /// left with no tag is untagged, a normal state.
    /// </summary>
    /// <response code="204">Deleted.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">No such tag.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var tag = await _database.Tags.FirstOrDefaultAsync(tag => tag.Id == id, cancellationToken);

        if (tag is null)
        {
            return NotFound();
        }

        await BinSynthesisOfAsync(tag, cancellationToken);
        _database.Tags.Remove(tag);

        await _database.SaveChangesAsync(CancellationToken.None);

        _notifier.Publish(new ChangeEvent(ChangeResources.Notes, ChangeActions.Updated));

        return NoContent();
    }

    // The synthesis is tied to the tag by name only (SynthesisGroup), so once the tag is
    // gone it describes a group that no longer exists.
    private async Task BinSynthesisOfAsync(Tag tag, CancellationToken cancellationToken)
    {
        var synthesis = await _database.Notes.FirstOrDefaultAsync(
            note => note.Kind == NoteKind.Synthesis && note.SynthesisGroup == tag.Name,
            cancellationToken);

        if (synthesis is not null)
        {
            synthesis.DeletedAtUtc = DateTime.UtcNow;
        }
    }
}
