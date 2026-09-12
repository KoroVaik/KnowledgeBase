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

    /// <summary>Lists jobs still Pending or Running, oldest first.</summary>
    /// <response code="200">The listing, possibly empty.</response>
    /// <response code="401">No session, or it has expired.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ActiveJobResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var jobs = await (
            from job in _database.ProcessingJobs
            where job.Status == ProcessingStatus.Pending || job.Status == ProcessingStatus.Running
            join asset in _database.Assets on job.AssetId equals asset.Id into assetJoin
            from asset in assetJoin.DefaultIfEmpty()
            orderby job.CreatedAtUtc
            select new ActiveJobResponse(
                job.Id,
                job.Kind.ToString(),
                job.Status.ToString(),
                job.AssetId,
                asset != null ? asset.OriginalFileName : null,
                job.CreatedAtUtc,
                job.StartedAtUtc,
                job.Attempts,
                job.Error))
            .ToListAsync(cancellationToken);

        return Ok(jobs);
    }
}
