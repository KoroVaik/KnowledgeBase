using KnowledgeBase.Api.Controllers.Tags.Contracts;
using KnowledgeBase.Core.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Controllers.Tags;

/// <summary>The tag vocabulary, used to browse notes by facet and to trigger a synthesis.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public sealed class TagsController(KnowledgeBaseDbContext database) : ControllerBase
{
    private readonly KnowledgeBaseDbContext _database = database;

    /// <summary>Lists every tag with its live Source-note count, busiest first.</summary>
    /// <response code="200">The listing, possibly empty.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TagResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
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

        var tags = await _database.Tags.ToListAsync(cancellationToken);

        var response = tags
            .Select(tag => new TagResponse(tag.Name, tag.Confirmed, counts.GetValueOrDefault(tag.Id)))
            .OrderByDescending(tag => tag.NoteCount)
            .ThenBy(tag => tag.Name)
            .ToList();

        return Ok(response);
    }
}
