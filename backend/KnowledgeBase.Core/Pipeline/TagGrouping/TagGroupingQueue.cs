using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Pipeline.TagGrouping;

// Puts a GroupTags job in front of the worker. Does not save.
public static class TagGroupingQueue
{
    // False when a grouping run is already pending or running - there is no payload to key on,
    // one at a time is enough.
    public static async Task<bool> EnqueueAsync(KnowledgeBaseDbContext database, CancellationToken cancellationToken)
    {
        var alreadyQueued = await database.ProcessingJobs
            .AnyAsync(
                job => job.Kind == JobKind.GroupTags
                    && (job.Status == ProcessingStatus.Pending || job.Status == ProcessingStatus.Running),
                cancellationToken);

        if (alreadyQueued)
        {
            return false;
        }

        database.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = JobKind.GroupTags,
            CreatedAtUtc = DateTime.UtcNow,
            Status = ProcessingStatus.Pending,
        });

        return true;
    }
}
