using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Pipeline.FaceAnalysis;

public static class FaceValidationQueue
{
    public static async Task<bool> EnqueueAsync(KnowledgeBaseDbContext database, string assetId, CancellationToken cancellationToken)
    {
        if (database.ProcessingJobs.Local.Any(job => job.Kind == JobKind.ValidateFaces && job.AssetId == assetId
                && job.Status is ProcessingStatus.Pending or ProcessingStatus.Running)) return false;
        return await ProcessingQueue.EnsurePendingAsync(database, assetId, JobKind.ValidateFaces, cancellationToken);
    }
}
