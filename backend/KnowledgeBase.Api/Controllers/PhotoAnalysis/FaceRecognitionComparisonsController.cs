using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.RealTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Controllers.PhotoAnalysis;

/// <summary>Runs face-recognition models over detected faces without changing person suggestions.</summary>
[ApiController]
[Authorize]
[Route("api/photo-analysis/face-recognition-comparisons")]
[Produces("application/json")]
public sealed class FaceRecognitionComparisonsController(KnowledgeBaseDbContext database, IChangeNotifier notifier) : ControllerBase
{
    /// <summary>Lists automatic recognition experiments, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery, Range(0, 100000)] int page = 0, CancellationToken cancellationToken = default)
    {
        var query = from run in database.FaceRecognitionComparisonRuns.AsNoTracking()
                    join job in database.ProcessingJobs on run.JobId equals job.Id
                    orderby run.CreatedAtUtc descending, run.Id descending
                    select new { run.Id, run.CreatedAtUtc, run.PhotoCount, run.ReferenceFaceCount, run.PersonCount, Status = job.Status.ToString() };
        var items = await query.Skip(page * 20).Take(21).ToListAsync(cancellationToken);
        return Ok(new
        {
            models = FaceRecognitionComparisonModels.All,
            pendingPhotoCount = await PendingPhotoCountAsync(cancellationToken),
            runs = items.Take(20), hasMore = items.Count > 20,
        });
    }

    /// <summary>Returns metrics and the few face pairs on which the completed models disagree or struggle.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var run = await database.FaceRecognitionComparisonRuns.AsNoTracking()
            .Include(item => item.Results).ThenInclude(result => result.Scores)
            .Include(item => item.Pairs).ThenInclude(pair => pair.Scores)
            .AsSplitQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (run is null) return NotFound();

        var job = await database.ProcessingJobs.AsNoTracking().SingleAsync(item => item.Id == run.JobId, cancellationToken);
        var occurrenceIds = run.Pairs.SelectMany(pair => new[] { pair.FirstFaceOccurrenceId, pair.SecondFaceOccurrenceId }).Distinct().ToList();
        var occurrences = await database.FaceOccurrences.AsNoTracking().Where(item => occurrenceIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var resultOrder = FaceRecognitionComparisonModels.All.Select(item => item.Id).ToList();
        return Ok(new
        {
            run.Id, run.CreatedAtUtc, run.PhotoCount, run.ReferenceFaceCount, run.PersonCount,
            status = job.Status.ToString(), error = job.Error,
            results = run.Results.OrderBy(result => resultOrder.IndexOf(result.ModelId)).Select(result => new
            {
                result.Id, result.ModelId, result.ModelName, result.Error, result.ElapsedMilliseconds, result.CompletedAtUtc, result.Threshold,
                result.SamePersonPairs, result.DifferentPersonPairs, result.TruePositives, result.FalsePositives, result.TrueNegatives, result.FalseNegatives,
                configuration = JsonSerializer.Deserialize<JsonElement>(result.ConfigurationJson),
            }),
            evidence = run.Pairs.OrderBy(pair => pair.Id).Select(pair => new
            {
                pair.Id, pair.IsSamePerson,
                first = OccurrenceResponse(occurrences[pair.FirstFaceOccurrenceId]),
                second = OccurrenceResponse(occurrences[pair.SecondFaceOccurrenceId]),
                scores = pair.Scores.Select(score => new { score.ResultId, score.Score, score.IsMatch }),
            }),
        });
    }

    /// <summary>Queues every recognizer for photos with at least one face still missing a model result. It does not detect faces or alter suggestions.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var pending = await (from run in database.FaceRecognitionComparisonRuns
                             join job in database.ProcessingJobs on run.JobId equals job.Id
                             where job.Status == ProcessingStatus.Pending || job.Status == ProcessingStatus.Running
                             select run.Id).FirstOrDefaultAsync(cancellationToken);
        if (pending is not null) return Ok(new { id = pending });

        var photoCount = await PendingPhotoCountAsync(cancellationToken);
        if (photoCount == 0) return Ok(new { id = (string?)null });

        var id = Guid.NewGuid().ToString("N");
        var jobId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        database.ProcessingJobs.Add(new ProcessingJob { Id = jobId, Kind = JobKind.CompareFaceRecognizers, CreatedAtUtc = now, Status = ProcessingStatus.Pending });
        database.FaceRecognitionComparisonRuns.Add(new FaceRecognitionComparisonRun
        {
            Id = id, JobId = jobId, CreatedAtUtc = now, PhotoCount = photoCount,
            Results = FaceRecognitionComparisonModels.All.Select(model => new FaceRecognitionComparisonResult
            {
                Id = Guid.NewGuid().ToString("N"), RunId = id, ModelId = model.Id, ModelName = model.Name,
            }).ToList(),
        });
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return Accepted(new { id });
    }

    private async Task<int> PendingPhotoCountAsync(CancellationToken cancellationToken)
    {
        var latestOccurrenceTimes = database.FaceOccurrences
            .GroupBy(occurrence => occurrence.AssetId)
            .Select(group => new { AssetId = group.Key, CreatedAtUtc = group.Max(occurrence => occurrence.CreatedAtUtc) });
        var newestFaces = await (
            from occurrence in database.FaceOccurrences
            join latest in latestOccurrenceTimes on new { occurrence.AssetId, occurrence.CreatedAtUtc }
                equals new { latest.AssetId, latest.CreatedAtUtc }
            select new { occurrence.Id, occurrence.AssetId }).ToListAsync(cancellationToken);
        var referenceFaces = await (
            from reference in database.PersonReferenceFaces
            join occurrence in database.FaceOccurrences on reference.FaceOccurrenceId equals occurrence.Id
            select new { occurrence.Id, occurrence.AssetId }).ToListAsync(cancellationToken);
        var faces = newestFaces.Concat(referenceFaces).DistinctBy(face => face.Id).ToList();
        if (faces.Count == 0) return 0;
        var completed = await database.FaceRecognitionComparisonEmbeddings
            .Where(embedding => faces.Select(face => face.Id).Contains(embedding.FaceOccurrenceId))
            .Select(embedding => new { embedding.FaceOccurrenceId, embedding.ModelId }).ToListAsync(cancellationToken);
        var completedKeys = completed.Select(embedding => $"{embedding.FaceOccurrenceId}:{embedding.ModelId}").ToHashSet();
        var modelIds = FaceRecognitionComparisonModels.All.Select(model => model.Id).ToList();
        return faces.Where(face => modelIds.Any(modelId => !completedKeys.Contains($"{face.Id}:{modelId}")))
            .Select(face => face.AssetId).Distinct().Count();
    }

    private static object OccurrenceResponse(FaceOccurrence occurrence) => new
    {
        occurrence.Id, occurrence.AssetId,
        bounds = new { occurrence.X, occurrence.Y, occurrence.Width, occurrence.Height },
    };
}
