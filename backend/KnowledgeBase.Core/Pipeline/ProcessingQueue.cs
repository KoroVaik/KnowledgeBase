using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Pipeline;

/// <summary>
/// Puts an asset back in front of the worker. Used by "process again" on a note and by the
/// same button on a file whose note has been binned.
/// </summary>
public static class ProcessingQueue
{
    /// <summary>
    /// Makes sure the asset has a job waiting. Does not save.
    /// </summary>
    /// <returns>False when a job is already queued or running, so the caller can say so.</returns>
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

        // Reset rather than insert: one job per asset is a unique index, and the row is the
        // record of the current run, not a history of them.
        job.Status = ProcessingStatus.Pending;
        job.Attempts = 0;
        job.StartedAtUtc = null;
        job.CompletedAtUtc = null;
        job.Error = null;

        return true;
    }
}
