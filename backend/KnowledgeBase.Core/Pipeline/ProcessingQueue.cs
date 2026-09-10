using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Pipeline;

/// <summary>Puts an asset back in front of the worker ("process again").</summary>
public static class ProcessingQueue
{
    /// <summary>Ensures the asset has a job waiting. Does not save.</summary>
    /// <returns>False when a job is already queued or running.</returns>
    public static async Task<bool> EnsurePendingAsync(
        KnowledgeBaseDbContext database,
        string assetId,
        CancellationToken cancellationToken)
    {
        var job = await database.ProcessingJobs
            .FirstOrDefaultAsync(existing => existing.AssetId == assetId, cancellationToken);

        if (job is null)
        {
            database.ProcessingJobs.Add(ProcessingJob.Queue(assetId));
            return true;
        }

        if (job.Status is ProcessingStatus.Pending or ProcessingStatus.Running)
        {
            return false;
        }

        // Reset, not insert: one job per asset (unique index); the row is the current run, not history.
        job.Status = ProcessingStatus.Pending;
        job.Attempts = 0;
        job.StartedAtUtc = null;
        job.CompletedAtUtc = null;
        job.Error = null;

        return true;
    }
}
