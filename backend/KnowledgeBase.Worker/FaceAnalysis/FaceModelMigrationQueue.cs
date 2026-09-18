using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Worker.FaceAnalysis;

// The self-healing trigger: whenever the stored face data no longer matches what the current
// code would produce (a model swap happened at some point), the worker queues one migration
// job for itself. Idempotent and deduplicated - repeated starts find nothing or find a job
// already waiting.
public static class FaceModelMigrationQueue
{
    public static async Task<bool> EnqueueIfGapAsync(KnowledgeBaseDbContext database, IFaceAnalyzer analyzer, CancellationToken cancellationToken)
    {
        var staleEmbeddings = await database.FaceOccurrences
            .Where(occurrence => occurrence.EmbeddingModelKey != analyzer.EmbeddingModelKey)
            .CountAsync(cancellationToken);
        var unassignedIdentities = await database.FaceOccurrences
            .CountAsync(occurrence => occurrence.IdentityId == null, cancellationToken);
        // Outdated means the photo has no detection of the current version at all; an older
        // version's run alone is history, not a gap (a re-detection already happened). Exact
        // duplicates are excluded - detection skips them by design, their old runs stay history.
        var currentVersionAssetIds = database.PhotoAnalysisRuns
            .Where(run => run.PipelineVersion == FaceAnalysisPipeline.CurrentDetectionVersion)
            .Select(run => run.AssetId);
        var outdatedAssetIds = (await database.PhotoAnalysisRuns
            .Where(run => run.PipelineVersion.StartsWith("face-analysis/"))
            .Select(run => run.AssetId)
            .Except(currentVersionAssetIds)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();
        var canonicalAssetIds = (await database.Assets.ToListAsync(cancellationToken))
            .GroupBy(asset => asset.ContentSha256 ?? asset.Id)
            .Select(group => group.OrderBy(asset => asset.UploadedAtUtc).ThenBy(asset => asset.Id).First().Id)
            .ToHashSet();
        var outdatedDetections = outdatedAssetIds.Count(canonicalAssetIds.Contains);
        if (staleEmbeddings == 0 && outdatedDetections == 0 && unassignedIdentities == 0) return false;

        var waiting = await database.ProcessingJobs.AnyAsync(
            job => job.Kind == JobKind.MigrateFaceModels
                && (job.Status == ProcessingStatus.Pending || job.Status == ProcessingStatus.Running),
            cancellationToken);
        if (waiting) return false;

        database.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid().ToString("N"), Kind = JobKind.MigrateFaceModels,
            CreatedAtUtc = DateTime.UtcNow, Status = ProcessingStatus.Pending
        });
        await database.SaveChangesAsync(cancellationToken);
        Console.WriteLine($"[face-migration] Gap found: {staleEmbeddings} stale embedding(s), {outdatedDetections} photo(s) with outdated detections, {unassignedIdentities} face(s) without identity — migration queued.");
        return true;
    }
}
