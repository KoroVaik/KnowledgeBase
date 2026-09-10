using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline.Extraction;
using KnowledgeBase.Core.RealTime;
using KnowledgeBase.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Core.Pipeline;

// Polls ProcessingJobs, turns each queued file into a Note. One instance for now, living
// inside the API process; splitting it into its own service later changes only where it is
// hosted, not what it does.
public sealed class PipelineWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PipelineOptions> options,
    ILogger<PipelineWorker> logger) : BackgroundService
{
    private readonly PipelineOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ResetStuckJobsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool worked;

            try
            {
                worked = await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Pipeline tick failed.");
                worked = false;
            }

            if (!worked)
            {
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
        }
    }

    // A worker that crashed mid-job left its row in Running. On a fresh start nothing is
    // actually running, so anything still marked so is safe to requeue. Multiple instances
    // would need a lease instead.
    private async Task ResetStuckJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<KnowledgeBaseDbContext>();

        var requeued = await database.ProcessingJobs
            .Where(job => job.Status == ProcessingStatus.Running)
            .ExecuteUpdateAsync(
                update => update.SetProperty(job => job.Status, ProcessingStatus.Pending),
                cancellationToken);

        if (requeued > 0)
        {
            logger.LogInformation("Requeued {Count} stuck job(s).", requeued);
        }
    }

    private async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var database = services.GetRequiredService<KnowledgeBaseDbContext>();

        var job = await ClaimAsync(database, cancellationToken);

        if (job is null)
        {
            return false;
        }

        var analyzer = services.GetRequiredService<IContentAnalyzer>();

        try
        {
            await analyzer.EnsureModelAvailableAsync(cancellationToken);
        }
        catch (InvalidOperationException error)
        {
            // Not the job's fault - hand it back untouched (no attempt spent) and wait.
            logger.LogWarning("Analyzer unavailable: {Reason}", error.Message);
            job.Status = ProcessingStatus.Pending;
            job.StartedAtUtc = null;
            await database.SaveChangesAsync(cancellationToken);
            await Task.Delay(_options.OutageDelay, cancellationToken);
            return false;
        }

        job.Attempts += 1;

        try
        {
            var note = await ProcessAsync(database, analyzer, services, job, cancellationToken);

            job.Status = ProcessingStatus.Done;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.Error = null;

            // One SaveChanges: the note, its links, the resolved dangling links and the job
            // status all commit together, or none do.
            await database.SaveChangesAsync(cancellationToken);

            services.GetRequiredService<IChangeNotifier>()
                .Publish(new ChangeEvent(ChangeResources.Notes, ChangeActions.Created, note.Id));
        }
        catch (ContentTooLargeException error)
        {
            // Not a failure to retry: the input cannot fit however many times we try it.
            logger.LogWarning("Job {JobId} skipped: {Reason}", job.Id, error.Message);

            job.Status = ProcessingStatus.Skipped;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.Error = error.Message;

            await database.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            var giveUp = job.Attempts >= _options.MaxAttempts;

            logger.LogError(
                error,
                "Job {JobId} failed on attempt {Attempts}{Final}.",
                job.Id,
                job.Attempts,
                giveUp ? " (final)" : string.Empty);

            job.Status = giveUp ? ProcessingStatus.Failed : ProcessingStatus.Pending;
            job.CompletedAtUtc = giveUp ? DateTime.UtcNow : null;
            job.Error = error.Message;

            await database.SaveChangesAsync(CancellationToken.None);
        }

        return true;
    }

    private static Task<ProcessingJob?> ClaimAsync(
        KnowledgeBaseDbContext database,
        CancellationToken cancellationToken)
    {
        // The connection retries on failure, and that execution strategy refuses a bare
        // BeginTransaction - the whole unit has to be handed to it so a retry replays all of it.
        var strategy = database.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

            // FOR UPDATE SKIP LOCKED: a second worker steps over this row instead of blocking on
            // it, and the lock is held until this transaction commits the Running flip. Raw SQL
            // because EF has no expression for it; LIMIT 1 is in the SQL and the result is
            // materialised as-is so EF does not wrap it in a composing subquery.
            var claimed = await database.ProcessingJobs
                .FromSqlRaw(
                    """
                    SELECT * FROM "ProcessingJobs"
                    WHERE "Status" = 'Pending'
                    ORDER BY "CreatedAtUtc"
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);

            var job = claimed.FirstOrDefault();

            if (job is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            job.Status = ProcessingStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return job;
        });
    }

    private static async Task<Note> ProcessAsync(
        KnowledgeBaseDbContext database,
        IContentAnalyzer analyzer,
        IServiceProvider services,
        ProcessingJob job,
        CancellationToken cancellationToken)
    {
        var asset = await database.Assets.FirstOrDefaultAsync(record => record.Id == job.AssetId, cancellationToken)
            ?? throw new InvalidOperationException($"Asset {job.AssetId} no longer exists.");

        var kind = ProcessableContent.Classify(asset.ContentType, asset.OriginalFileName)
            ?? throw new InvalidOperationException($"{asset.OriginalFileName} is not a supported type.");

        var reader = services.GetRequiredService<IAssetContentReader>();
        var extractor = services.GetRequiredService<SourceExtractorSelector>().For(kind);
        var maxSourceChars = services.GetRequiredService<IOptions<PipelineOptions>>().Value.MaxSourceChars;

        var titles = await database.Notes.Select(note => note.Title).ToListAsync(cancellationToken);
        var categories = await database.Notes.Select(note => note.Category).Distinct().ToListAsync(cancellationToken);

        var bytes = await reader.ReadBytesAsync(asset.StoredFileName, cancellationToken);
        var extracted = await extractor.ExtractAsync(
            new SourceAsset(bytes, asset.ContentType, asset.OriginalFileName), cancellationToken);

        // Caught here rather than left to Ollama's silent truncation: a bounded slice of a huge
        // document would produce a note that looks fine but was written from a fraction of the
        // source. Applies to every text-yielding kind - a .txt, a PDF's text layer, later a
        // transcript. Skip it and let the user split the file.
        if (extracted.Text is { Length: var length } && length > maxSourceChars)
        {
            throw new ContentTooLargeException(
                $"The text is {length:N0} characters; the pipeline handles up to "
                + $"{maxSourceChars:N0}. Split it into smaller files.");
        }

        var request = new AnalysisRequest(titles, categories, extracted.Text, extracted.Image);

        var result = await analyzer.AnalyzeAsync(request, cancellationToken);

        // The model is asked to link only to titles it was given, but a 14B model does not
        // always obey - it invents titles, and it links a note to itself. Keep only links to
        // notes that actually existed before this one.
        var known = titles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var links = result.Links
            .Where(known.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var now = DateTime.UtcNow;
        var note = new Note
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = result.Title,
            Category = result.Category,
            Body = AppendRelated(result.MarkdownBody, links),
            SourceAssetId = asset.Id,
            SourceFileName = asset.OriginalFileName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        database.Notes.Add(note);

        foreach (var target in links)
        {
            var targetId = await database.Notes
                .Where(existing => existing.Title == target)
                .Select(existing => existing.Id)
                .FirstOrDefaultAsync(cancellationToken);

            database.NoteLinks.Add(new NoteLink
            {
                Id = Guid.NewGuid().ToString("N"),
                SourceNoteId = note.Id,
                TargetTitle = target,
                TargetNoteId = targetId,
            });
        }

        // Links elsewhere that named this note before it existed. Loaded as tracked entities so
        // the update rides the same SaveChanges as the insert above.
        var dangling = await database.NoteLinks
            .Where(link => link.TargetNoteId == null && link.TargetTitle == note.Title)
            .ToListAsync(cancellationToken);

        foreach (var link in dangling)
        {
            link.TargetNoteId = note.Id;
        }

        return note;
    }

    private static string AppendRelated(string body, IReadOnlyList<string> links)
    {
        if (links.Count == 0)
        {
            return body;
        }

        var list = string.Join("\n", links.Select(title => $"- [[{title}]]"));

        return $"{body.TrimEnd()}\n\n## Related\n\n{list}\n";
    }
}
