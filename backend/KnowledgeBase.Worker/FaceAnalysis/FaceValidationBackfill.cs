using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline.FaceAnalysis;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Worker.FaceAnalysis;

public static class FaceValidationBackfill
{
    public static async Task<int> EnqueueAsync(KnowledgeBaseDbContext database, string model, CancellationToken cancellationToken)
    {
        var configurationHash = FaceValidationPrompt.ConfigurationHash(model);
        var states = await FaceIdentityState.LoadAsync(database, cancellationToken);
        var validationState = await FaceValidationPolicy.LoadAsync(database, cancellationToken);
        var assets = (await database.Assets.ToListAsync(cancellationToken))
            .GroupBy(asset => asset.ContentSha256 ?? asset.Id)
            .Select(group => group.OrderBy(asset => asset.UploadedAtUtc).ThenBy(asset => asset.Id).First()).ToDictionary(asset => asset.Id);
        var faces = await (from face in database.FaceOccurrences
            join run in database.PhotoAnalysisRuns on face.RunId equals run.Id
            where run.PipelineVersion == FaceAnalysisPipeline.CurrentDetectionVersion && face.IdentityId != null
            select face).ToListAsync(cancellationToken);
        var pendingAssets = new HashSet<string>();
        foreach (var face in faces.GroupBy(face => face.IdentityId!).Select(group => group.OrderByDescending(face => face.CreatedAtUtc).First()))
        {
            if (!assets.TryGetValue(face.AssetId, out var asset) || states.SettledIds.Contains(face.IdentityId!)
                || states.IgnoredGroupIds.ContainsKey(face.IdentityId!) || validationState.Decisions.ContainsKey(face.IdentityId!)) continue;
            var validation = validationState.Validations.GetValueOrDefault(face.Id);
            if (validation?.CompletedAtUtc is not null && validation.ConfigurationHash == configurationHash
                && validation.InputHash == FaceValidationImages.InputHash(face, asset.ContentSha256 ?? asset.Id)) continue;
            if (validation?.CompletedAtUtc is not null)
            {
                var stale = await database.FaceValidations.SingleAsync(item => item.FaceOccurrenceId == face.Id, cancellationToken);
                stale.CompletedAtUtc = null;
                stale.Subject = null;
                stale.Evidence = null;
                stale.LastError = null;
            }
            pendingAssets.Add(face.AssetId);
        }
        if (pendingAssets.Count == 0) return 0;
        // Put pending faces into Unsorted before the slower validation jobs begin.
        await ClusterFacesQueue.EnqueueAsync(database, cancellationToken);
        foreach (var assetId in pendingAssets) await FaceValidationQueue.EnqueueAsync(database, assetId, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return pendingAssets.Count;
    }
}
