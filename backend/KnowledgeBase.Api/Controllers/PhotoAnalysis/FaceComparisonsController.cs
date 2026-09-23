using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.RealTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Controllers.PhotoAnalysis;

/// <summary>Independent detector experiments and manual quality labels, without changing person suggestions.</summary>
[ApiController]
[Authorize]
[Route("api/photo-analysis/face-comparisons")]
[Produces("application/json")]
public sealed class FaceComparisonsController(KnowledgeBaseDbContext database, IChangeNotifier notifier) : ControllerBase
{
    /// <summary>Lists comparison history, newest first, optionally filtered to one photo.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? assetId, [FromQuery] bool showReviewed = false, [FromQuery, Range(0, 100000)] int page = 0, CancellationToken cancellationToken = default)
    {
        var expectedModelCount = FaceComparisonModels.All.Count;
        var query = from run in database.FaceComparisonRuns.AsNoTracking()
                    join asset in database.Assets on run.AssetId equals asset.Id
                    join job in database.ProcessingJobs on run.JobId equals job.Id
                    where (assetId == null || run.AssetId == assetId) && job.Status == ProcessingStatus.Done
                        && run.Results.Count == expectedModelCount
                        && !run.Results.Any(result => result.CompletedAtUtc == null || result.Error != null)
                        && (showReviewed || (!run.IsSkipped && run.ReviewedAtUtc == null))
                    orderby run.CreatedAtUtc descending, run.Id descending
                    select new { run.Id, run.AssetId, AssetName = asset.OriginalFileName, run.CreatedAtUtc, run.IsSkipped, run.ReviewedAtUtc, Status = job.Status.ToString() };
        var items = await query.Skip(page * 20).Take(21).ToListAsync(cancellationToken);
        return Ok(new { models = FaceComparisonModels.All, runs = items.Take(20), hasMore = items.Count > 20 });
    }

    /// <summary>Returns all model outputs, crops' coordinates and review labels for one comparison.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var run = await database.FaceComparisonRuns.AsNoTracking().Include(item => item.Results).ThenInclude(item => item.Detections)
            .AsSplitQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (run is null) return NotFound();
        var job = await database.ProcessingJobs.AsNoTracking().SingleAsync(item => item.Id == run.JobId, cancellationToken);
        return Ok(new
        {
            run.Id, run.AssetId, run.CreatedAtUtc, run.ImageWidth, run.ImageHeight, run.ContentSha256, run.IsSkipped, run.ReviewedAtUtc,
            status = job.Status.ToString(), error = job.Error,
            results = run.Results.OrderBy(result => Array.FindIndex(FaceComparisonModels.All.ToArray(), model => model.Id == result.ModelId))
                .Select(result => new
                {
                    result.Id, result.ModelId, result.ModelName, result.Error, result.ElapsedMilliseconds, result.CompletedAtUtc, result.MissedFaces,
                    configuration = JsonSerializer.Deserialize<JsonElement>(result.ConfigurationJson),
                    detections = result.Detections.OrderBy(face => face.Ordinal).Select(face => new
                    {
                        face.Id, face.Ordinal, bounds = new ComparisonBounds(face.X, face.Y, face.Width, face.Height), face.Score, face.IsFace,
                        landmarks = JsonSerializer.Deserialize<ComparisonLandmark[]>(face.LandmarksJson),
                        warnings = JsonSerializer.Deserialize<string[]>(face.WarningsJson),
                    }),
                }),
        });
    }

    /// <summary>Queues all three detectors for a photo. Completed experiments remain in history.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateFaceComparisonRequest request, CancellationToken cancellationToken)
    {
        var asset = await database.Assets.SingleOrDefaultAsync(item => item.Id == request.AssetId, cancellationToken);
        if (asset is null) return NotFound();
        if (ProcessableContent.Classify(asset.ContentType, asset.OriginalFileName) != ContentKind.Image)
            return BadRequest(new { error = "Choose an image to compare." });
        var pending = await (from run in database.FaceComparisonRuns
                             join job in database.ProcessingJobs on run.JobId equals job.Id
                             where run.AssetId == asset.Id && (job.Status == ProcessingStatus.Pending || job.Status == ProcessingStatus.Running)
                             select run.Id).FirstOrDefaultAsync(cancellationToken);
        if (pending is not null) return Ok(new { id = pending });
        var jobId = Guid.NewGuid().ToString("N");
        var id = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        database.ProcessingJobs.Add(new ProcessingJob { Id = jobId, Kind = JobKind.CompareFaceDetectors, CreatedAtUtc = now, Status = ProcessingStatus.Pending });
        database.FaceComparisonRuns.Add(new FaceComparisonRun
        {
            Id = id, AssetId = asset.Id, JobId = jobId, CreatedAtUtc = now,
            Results = FaceComparisonModels.All.Select(model => new FaceComparisonResult
            {
                Id = Guid.NewGuid().ToString("N"), RunId = id, ModelId = model.Id, ModelName = model.Name,
            }).ToList(),
        });
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return Accepted(new { id });
    }

    /// <summary>Labels a detection as a real face or a false positive; null clears the label.</summary>
    [HttpPut("detections/{id}/review")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Review(string id, ReviewFaceDetectionRequest request, CancellationToken cancellationToken)
    {
        var detection = await database.FaceComparisonDetections.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (detection is null) return NotFound();
        detection.IsFace = request.IsFace;
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return NoContent();
    }

    /// <summary>Labels several detections from one experiment at once; used when one candidate face spans model outputs.</summary>
    [HttpPut("{id}/detections/review")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReviewMany(string id, ReviewFaceDetectionsRequest request, CancellationToken cancellationToken)
    {
        var requested = request.Reviews.GroupBy(review => review.Id).ToDictionary(group => group.Key, group => group.Last().IsFace);
        if (requested.Count == 0) return BadRequest(new { error = "Choose at least one detection." });
        var detections = await (
            from detection in database.FaceComparisonDetections
            join result in database.FaceComparisonResults on detection.ResultId equals result.Id
            where result.RunId == id && requested.Keys.Contains(detection.Id)
            select detection).ToListAsync(cancellationToken);
        if (detections.Count != requested.Count) return BadRequest(new { error = "Every detection must belong to this comparison." });
        foreach (var detection in detections) detection.IsFace = requested[detection.Id];
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return NoContent();
    }

    /// <summary>Records the manually counted missed faces for a model; null means not checked.</summary>
    [HttpPut("results/{id}/missed-faces")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Missed(string id, MissedFacesRequest request, CancellationToken cancellationToken)
    {
        var result = await database.FaceComparisonResults.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (result is null) return NotFound();
        if (result.CompletedAtUtc is null || result.Error is not null)
            return Conflict(new { error = "Wait for this detector to finish successfully." });
        result.MissedFaces = request.Count;
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return NoContent();
    }

    /// <summary>Sets the count of faces missed by every detector in one experiment.</summary>
    [HttpPut("{id}/missed-faces")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MissedForAll(string id, MissedFacesRequest request, CancellationToken cancellationToken)
    {
        var results = await database.FaceComparisonResults.Where(result => result.RunId == id).ToListAsync(cancellationToken);
        if (results.Count == 0) return NotFound();
        if (results.Any(result => result.CompletedAtUtc is null || result.Error is not null))
            return Conflict(new { error = "Wait for every detector to finish successfully." });
        foreach (var result in results) result.MissedFaces = request.Count;
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return NoContent();
    }

    /// <summary>Excludes or restores one unusable photo without deleting its detector outputs.</summary>
    [HttpPut("{id}/skipped")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetSkipped(string id, SetComparisonSkippedRequest request, CancellationToken cancellationToken)
    {
        var run = await database.FaceComparisonRuns.SingleOrDefaultAsync(run => run.Id == id, cancellationToken);
        if (run is null) return NotFound();
        run.IsSkipped = request.IsSkipped;
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return NoContent();
    }

    /// <summary>Marks a completed detector experiment as inspected, without changing corrected labels.</summary>
    [HttpPut("{id}/reviewed")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetReviewed(string id, SetComparisonReviewedRequest request, CancellationToken cancellationToken)
    {
        var run = await database.FaceComparisonRuns.Include(item => item.Results)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (run is null) return NotFound();
        if (request.IsReviewed)
        {
            var status = await database.ProcessingJobs.Where(job => job.Id == run.JobId)
                .Select(job => job.Status).SingleAsync(cancellationToken);
            if (status != ProcessingStatus.Done || run.IsSkipped || run.Results.Count != FaceComparisonModels.All.Count
                || run.Results.Any(result => result.CompletedAtUtc is null || result.Error is not null))
                return Conflict(new { error = "Wait for every detector to finish successfully before reviewing the photo." });
        }
        run.ReviewedAtUtc = request.IsReviewed ? DateTime.UtcNow : null;
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return NoContent();
    }
}

public sealed record CreateFaceComparisonRequest([Required, StringLength(32, MinimumLength = 32)] string AssetId);
public sealed record ReviewFaceDetectionRequest(bool? IsFace);
public sealed record ReviewFaceDetectionsRequest([Required] IReadOnlyList<ReviewFaceDetection> Reviews);
public sealed record ReviewFaceDetection([Required, StringLength(32, MinimumLength = 32)] string Id, bool IsFace);
public sealed record MissedFacesRequest([Range(0, 10000)] int? Count);
public sealed record SetComparisonSkippedRequest(bool IsSkipped);
public sealed record SetComparisonReviewedRequest(bool IsReviewed);
