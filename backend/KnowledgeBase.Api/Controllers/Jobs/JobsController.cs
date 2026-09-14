using KnowledgeBase.Api.Controllers.Jobs.Contracts;
using KnowledgeBase.Core.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Controllers.Jobs;

/// <summary>Worker status: what the pipeline has queued up and what it is doing right now.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public sealed class JobsController(KnowledgeBaseDbContext database) : ControllerBase
{
    private readonly KnowledgeBaseDbContext _database = database;

    /// <summary>Lists jobs still Pending or Running, oldest first, each with a short description of its kind.</summary>
    /// <response code="200">The listing, possibly empty.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ActiveJobResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var rows = await (
            from job in _database.ProcessingJobs
            where job.Status == ProcessingStatus.Pending || job.Status == ProcessingStatus.Running
            join asset in _database.Assets on job.AssetId equals asset.Id into assetJoin
            from asset in assetJoin.DefaultIfEmpty()
            orderby job.CreatedAtUtc
            select new { Job = job, AssetFileName = asset != null ? asset.OriginalFileName : null })
            .ToListAsync(cancellationToken);

        var jobs = rows.Select(row => new ActiveJobResponse(
            row.Job.Id,
            row.Job.Kind.ToString(),
            JobKindDescriptions.For(row.Job.Kind),
            row.Job.Status.ToString(),
            row.Job.AssetId,
            row.AssetFileName,
            row.Job.CreatedAtUtc,
            row.Job.StartedAtUtc,
            row.Job.Attempts,
            row.Job.Error));

        return Ok(jobs);
    }

    /// <summary>
    /// When a job of any of the given kinds last finished. <c>Skipped</c> counts as a finished run
    /// (the job had nothing to do); <c>Failed</c> does not.
    /// </summary>
    /// <param name="kind">One or more job kinds, e.g. <c>?kind=GroupTags&amp;kind=SuggestTagParents</c>.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">The completion time, null when no such job has finished yet.</response>
    /// <response code="400">No kind given, or an unknown one.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet("last-completed")]
    [ProducesResponseType(typeof(LastCompletedJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LastCompleted([FromQuery] JobKind[] kind, CancellationToken cancellationToken)
    {
        if (kind.Length == 0)
        {
            return BadRequest(new { error = "Give at least one job kind." });
        }

        var completedAtUtc = await _database.ProcessingJobs
            .Where(job => kind.Contains(job.Kind)
                && (job.Status == ProcessingStatus.Done || job.Status == ProcessingStatus.Skipped))
            .MaxAsync(job => job.CompletedAtUtc, cancellationToken);

        return Ok(new LastCompletedJobResponse(completedAtUtc));
    }
}
