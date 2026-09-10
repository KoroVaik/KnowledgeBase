using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.RealTime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Core.Pipeline;

// Polls ProcessingJobs, hands each one to its IPipelineHandler, owns the retry / Skipped /
// Failed / SSE plumbing around it. See docs/ai-pipeline.md.
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

        try
        {
            await services.GetRequiredService<IContentAnalyzer>().EnsureModelAvailableAsync(cancellationToken);
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
            var handler = services.GetRequiredService<PipelineHandlerSelector>().For(job.Kind);
            var note = await handler.HandleAsync(job, cancellationToken);

            job.Status = ProcessingStatus.Done;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.Error = null;

            // One SaveChanges: note, links, tags, dangling-link fixes, job status - all or nothing.
            await database.SaveChangesAsync(cancellationToken);

            services.GetRequiredService<IChangeNotifier>()
                .Publish(new ChangeEvent(ChangeResources.Notes, ChangeActions.Created, note.Id));
        }
        catch (SkippableContentException error)
        {
            // Not retriable: a scan with no text, an empty file, a source past the context
            // window - retrying the same input cannot help.
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
}
