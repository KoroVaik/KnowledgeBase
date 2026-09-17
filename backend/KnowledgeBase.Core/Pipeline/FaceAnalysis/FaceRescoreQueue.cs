using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Pipeline.FaceAnalysis;

/// <summary>Asks the worker to re-rank unreviewed faces after the confirmed reference set changed.</summary>
public static class FaceRescoreQueue
{
    // Only one waiting job: reviewing a stack of faces in a row should cost one re-score, not one
    // per click. A job already Running is not counted - it read the references before this change.
    public static async Task<bool> EnqueueAsync(KnowledgeBaseDbContext database, CancellationToken cancellationToken)
    {
        var waiting = await database.ProcessingJobs.AnyAsync(
            job => job.Kind == JobKind.RescoreFaces && job.Status == ProcessingStatus.Pending,
            cancellationToken);
        if (waiting) return false;

        database.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid().ToString("N"), Kind = JobKind.RescoreFaces,
            CreatedAtUtc = DateTime.UtcNow, Status = ProcessingStatus.Pending
        });
        return true;
    }
}
