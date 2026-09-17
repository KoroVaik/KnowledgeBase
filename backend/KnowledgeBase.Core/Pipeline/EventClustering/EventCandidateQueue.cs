using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Pipeline.EventClustering;

public static class EventCandidateQueue
{
    public static async Task<bool> EnqueueAsync(KnowledgeBaseDbContext database, CancellationToken cancellationToken)
    {
        var pending = await database.ProcessingJobs.AnyAsync(
            job => job.Kind == JobKind.AnalyzeEventCandidates
                && (job.Status == ProcessingStatus.Pending || job.Status == ProcessingStatus.Running),
            cancellationToken);
        if (pending) return false;

        database.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid().ToString("N"), Kind = JobKind.AnalyzeEventCandidates,
            CreatedAtUtc = DateTime.UtcNow, Status = ProcessingStatus.Pending
        });
        return true;
    }
}
