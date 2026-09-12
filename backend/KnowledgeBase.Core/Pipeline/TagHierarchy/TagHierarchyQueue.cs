using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Pipeline.TagHierarchy;

// Puts a SuggestTagParents job in front of the worker. Does not save.
public static class TagHierarchyQueue
{
    // False when a placement run is already pending or running - there is no payload to key on,
    // one at a time is enough.
    public static async Task<bool> EnqueueAsync(KnowledgeBaseDbContext database, CancellationToken cancellationToken)
    {
        var alreadyQueued = await database.ProcessingJobs
            .AnyAsync(
                job => job.Kind == JobKind.SuggestTagParents
                    && (job.Status == ProcessingStatus.Pending || job.Status == ProcessingStatus.Running),
                cancellationToken);

        if (alreadyQueued)
        {
            return false;
        }

        database.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = JobKind.SuggestTagParents,
            CreatedAtUtc = DateTime.UtcNow,
            Status = ProcessingStatus.Pending,
        });

        return true;
    }
}
