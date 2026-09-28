using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Pipeline.SceneAnalysis;

/// <summary>Asks the worker to regroup every open scene after embeddings or review states changed.</summary>
public static class ClusterScenesQueue
{
    // Only one waiting job: reviewing several rows in a row should cost one regrouping, not one
    // per click. A job already Running is not counted - it read the review states before this change.
    public static async Task<bool> EnqueueAsync(KnowledgeBaseDbContext database, CancellationToken cancellationToken)
    {
        var waiting = await database.ProcessingJobs.AnyAsync(
            job => job.Kind == JobKind.ClusterScenes && job.Status == ProcessingStatus.Pending,
            cancellationToken);
        if (waiting) return false;

        database.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = JobKind.ClusterScenes,
            CreatedAtUtc = DateTime.UtcNow,
            Status = ProcessingStatus.Pending
        });
        return true;
    }
}
