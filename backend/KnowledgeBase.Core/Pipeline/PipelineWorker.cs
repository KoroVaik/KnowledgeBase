using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline.Extraction;
using KnowledgeBase.Core.RealTime;
using KnowledgeBase.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Core.Pipeline;

// Polls ProcessingJobs, turns each queued file into a Note. See docs/ai-pipeline.md.
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

    // A crash mid-job leaves a row in Running; on a fresh start nothing runs, so requeue it.
    // Multiple instances would need a lease instead.
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

            // One SaveChanges: note, links, dangling-link fixes, job status - all or nothing.
            await database.SaveChangesAsync(cancellationToken);

            services.GetRequiredService<IChangeNotifier>()
                .Publish(new ChangeEvent(ChangeResources.Notes, ChangeActions.Created, note.Id));
        }
        catch (ContentTooLargeException error)
        {
            // Not retriable: the input will not fit however many times we try.
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
        // The retry strategy refuses a bare BeginTransaction - hand it the whole unit so a
        // retry replays all of it.
        var strategy = database.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

            // FOR UPDATE SKIP LOCKED: a second worker steps over this row; the lock holds until
            // the Running flip commits. Raw SQL - EF has no expression for it - and materialised
            // as-is so EF adds no composing subquery.
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

        // A re-run replaces the note, not adds a sibling; the old one is not offered as a link target.
        var previous = await database.Notes
            .FirstOrDefaultAsync(existing => existing.SourceAssetId == asset.Id, cancellationToken);

        var titles = await database.Notes.Select(note => note.Title).ToListAsync(cancellationToken);
        var categories = await database.Notes.Select(note => note.Category).Distinct().ToListAsync(cancellationToken);

        if (previous is not null)
        {
            titles.Remove(previous.Title);
        }

        var bytes = await reader.ReadBytesAsync(asset.StoredFileName, cancellationToken);
        var extracted = await extractor.ExtractAsync(
            new SourceAsset(bytes, asset.ContentType, asset.OriginalFileName), cancellationToken);

        // Caught here, not left to Ollama's silent truncation: a bounded slice would make a
        // note that looks fine but came from a fraction of the source.
        if (extracted.Text is { Length: var length } && length > maxSourceChars)
        {
            throw new ContentTooLargeException(
                $"The text is {length:N0} characters; the pipeline handles up to "
                + $"{maxSourceChars:N0}. Split it into smaller files.");
        }

        var request = new AnalysisRequest(titles, categories, extracted.Text, extracted.Image);

        var result = await analyzer.AnalyzeAsync(request, cancellationToken);

        // A 14B model does not obey "link only to these titles" - it invents titles and
        // self-links. Keep only links to notes that existed before this one.
        var known = titles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var links = result.Links
            .Where(known.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var now = DateTime.UtcNow;

        if (previous is not null)
        {
            // On its own: a re-run usually reuses the title, and the live-title unique index
            // would reject the insert if EF ordered it before this update.
            previous.DeletedAtUtc = now;
            await database.SaveChangesAsync(cancellationToken);
        }

        var note = new Note
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = NoteKind.Source,
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

        // Links elsewhere that named this note before it existed. Tracked, so the fix rides
        // the insert's SaveChanges.
        var dangling = await database.NoteLinks
            .Where(link => link.TargetNoteId == null && link.TargetTitle == note.Title)
            .ToListAsync(cancellationToken);

        foreach (var link in dangling)
        {
            link.TargetNoteId = note.Id;
        }

        if (previous is not null)
        {
            // Inbound links follow the note, not the version. Only the id moves - TargetTitle
            // is literal text in the other body.
            var inherited = await database.NoteLinks
                .Where(link => link.TargetNoteId == previous.Id)
                .ToListAsync(cancellationToken);

            foreach (var link in inherited)
            {
                link.TargetNoteId = note.Id;
            }
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
