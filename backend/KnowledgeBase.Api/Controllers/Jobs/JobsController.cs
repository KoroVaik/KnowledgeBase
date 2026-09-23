using KnowledgeBase.Api.Controllers.Jobs.Contracts;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.RealTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Controllers.Jobs;

/// <summary>Worker status: what the pipeline has queued up and what it is doing right now.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public sealed class JobsController(KnowledgeBaseDbContext database, IChangeNotifier notifier) : ControllerBase
{
    private readonly KnowledgeBaseDbContext _database = database;
    private readonly IChangeNotifier _notifier = notifier;

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

    /// <summary>Returns active and failed jobs together for the five-second status refresh.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(JobsSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var rows = await (
            from job in _database.ProcessingJobs.AsNoTracking()
            where job.Status == ProcessingStatus.Pending || job.Status == ProcessingStatus.Running || job.Status == ProcessingStatus.Failed
            join asset in _database.Assets on job.AssetId equals asset.Id into assetJoin
            from asset in assetJoin.DefaultIfEmpty()
            select new { Job = job, AssetFileName = asset != null ? asset.OriginalFileName : null })
            .ToListAsync(cancellationToken);
        var active = rows.Where(row => row.Job.Status != ProcessingStatus.Failed).OrderBy(row => row.Job.CreatedAtUtc)
            .Select(row => new ActiveJobResponse(row.Job.Id, row.Job.Kind.ToString(), JobKindDescriptions.For(row.Job.Kind),
                row.Job.Status.ToString(), row.Job.AssetId, row.AssetFileName, row.Job.CreatedAtUtc,
                row.Job.StartedAtUtc, row.Job.Attempts, row.Job.Error)).ToArray();
        var failed = rows.Where(row => row.Job.Status == ProcessingStatus.Failed).OrderByDescending(row => row.Job.CompletedAtUtc)
            .Select(row => new FailedJobResponse(row.Job.Id, row.Job.Kind.ToString(), JobKindDescriptions.For(row.Job.Kind),
                row.Job.AssetId, row.AssetFileName, row.Job.CreatedAtUtc, row.Job.CompletedAtUtc, row.Job.Attempts, row.Job.Error)).ToArray();
        return Ok(new JobsSummaryResponse(active, failed));
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

    /// <summary>Lists jobs the worker gave up on, most recently failed first.</summary>
    /// <response code="200">The listing, possibly empty.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet("failed")]
    [ProducesResponseType(typeof(IReadOnlyList<FailedJobResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListFailed(CancellationToken cancellationToken)
    {
        var rows = await (
            from job in _database.ProcessingJobs
            where job.Status == ProcessingStatus.Failed
            join asset in _database.Assets on job.AssetId equals asset.Id into assetJoin
            from asset in assetJoin.DefaultIfEmpty()
            orderby job.CompletedAtUtc descending
            select new { Job = job, AssetFileName = asset != null ? asset.OriginalFileName : null })
            .ToListAsync(cancellationToken);

        var jobs = rows.Select(row => new FailedJobResponse(
            row.Job.Id,
            row.Job.Kind.ToString(),
            JobKindDescriptions.For(row.Job.Kind),
            row.Job.AssetId,
            row.AssetFileName,
            row.Job.CreatedAtUtc,
            row.Job.CompletedAtUtc,
            row.Job.Attempts,
            row.Job.Error));

        return Ok(jobs);
    }

    /// <summary>Puts one failed job back in the queue with a fresh set of attempts.</summary>
    /// <param name="id">The job id.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="202">Queued.</response>
    /// <response code="401">No session, or it has expired.</response>
    /// <response code="404">No such job.</response>
    /// <response code="409">The job has not failed (it is queued, running, done or skipped).</response>
    [HttpPost("{id}/retry")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(string id, CancellationToken cancellationToken)
    {
        var job = await _database.ProcessingJobs.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (job is null)
        {
            return NotFound();
        }

        if (job.Status != ProcessingStatus.Failed)
        {
            return Conflict(new { error = "Only a failed job can be retried." });
        }

        Requeue(job);
        await _database.SaveChangesAsync(CancellationToken.None);
        _notifier.Publish(new ChangeEvent(ChangeResources.Assets, ChangeActions.Updated));

        return Accepted();
    }

    /// <summary>Puts every failed job back in the queue with a fresh set of attempts.</summary>
    /// <response code="200">How many jobs were re-queued, possibly zero.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpPost("failed/retry")]
    [ProducesResponseType(typeof(RetryFailedJobsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RetryAllFailed(CancellationToken cancellationToken)
    {
        var jobs = await _database.ProcessingJobs
            .Where(job => job.Status == ProcessingStatus.Failed)
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            Requeue(job);
        }

        if (jobs.Count > 0)
        {
            await _database.SaveChangesAsync(CancellationToken.None);
            _notifier.Publish(new ChangeEvent(ChangeResources.Assets, ChangeActions.Updated));
        }

        return Ok(new RetryFailedJobsResponse(jobs.Count));
    }

    private static void Requeue(ProcessingJob job)
    {
        job.Status = ProcessingStatus.Pending;
        job.Attempts = 0;
        job.Error = null;
        job.StartedAtUtc = null;
        job.CompletedAtUtc = null;
    }
}
