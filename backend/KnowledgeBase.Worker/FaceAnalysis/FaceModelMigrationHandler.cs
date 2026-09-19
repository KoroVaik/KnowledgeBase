using System.Text.Json;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.Pipeline.FaceAnalysis;
using KnowledgeBase.Core.Storage;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace KnowledgeBase.Worker.FaceAnalysis;

// One idempotent migration pass, repeated model swaps included: re-embeds every face whose
// stored EmbeddingModelKey is not the current embedder, assigns face identities to occurrences
// still without one, requeues detection for photos whose stored detections come from an older
// pipeline version, retires proposals on already-settled identities, and regroups the open faces.
// The worker enqueues it itself on start (FaceModelMigrationQueue) whenever the gap check finds
// anything.
public sealed class FaceModelMigrationHandler(
    KnowledgeBaseDbContext database,
    IAssetContentReader reader,
    IFaceAnalyzer analyzer,
    ArcFaceEmbedder embedder) : IPipelineHandler
{
    public JobKind Kind => JobKind.MigrateFaceModels;

    public bool RequiresContentAnalyzer => false;

    public async Task<Note?> HandleAsync(CancellationToken cancellationToken)
    {
        await ReembedStaleFacesAsync(cancellationToken);
        await AssignFaceIdentitiesAsync(cancellationToken);
        await RequeueOutdatedDetectionsAsync(cancellationToken);
        await SupersedeSettledIdentityProposalsAsync(cancellationToken);
        if (await ClusterFacesQueue.EnqueueAsync(database, cancellationToken)) await database.SaveChangesAsync(cancellationToken);
        return null;
    }

    public Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken) => HandleAsync(cancellationToken);

    private async Task ReembedStaleFacesAsync(CancellationToken cancellationToken)
    {
        var stale = await database.FaceOccurrences
            .Where(occurrence => occurrence.EmbeddingModelKey != analyzer.EmbeddingModelKey)
            .OrderBy(occurrence => occurrence.AssetId)
            .ToListAsync(cancellationToken);
        var byAsset = stale.GroupBy(occurrence => occurrence.AssetId).ToList();
        if (byAsset.Count == 0) return;

        Console.WriteLine($"[face-migration] Re-embedding {stale.Count} face(s) on {byAsset.Count} photo(s) with {analyzer.EmbeddingModelKey}.");
        var assets = await database.Assets.ToDictionaryAsync(asset => asset.Id, cancellationToken);
        var done = 0;
        foreach (var group in byAsset)
        {
            if (!assets.TryGetValue(group.Key, out var asset))
            {
                Console.WriteLine($"[face-migration] Asset {group.Key} no longer exists — {group.Count()} face(s) left untouched.");
                continue;
            }

            using var image = Image.Load<Rgb24>(await reader.ReadBytesAsync(asset.StoredFileName, cancellationToken));
            image.Mutate(context => context.AutoOrient());
            foreach (var occurrence in group)
            {
                var landmarks = JsonSerializer.Deserialize<List<FaceLandmark>>(occurrence.LandmarksJson);
                if (landmarks is not { Count: 5 })
                {
                    Console.WriteLine($"[face-migration] Face {occurrence.Id} has no usable landmarks — skipped.");
                    continue;
                }
                occurrence.Embedding = embedder.Embed(image, landmarks);
                occurrence.EmbeddingModelKey = analyzer.EmbeddingModelKey;
                done++;
            }
            await database.SaveChangesAsync(cancellationToken);
        }
        Console.WriteLine($"[face-migration] Re-embedded {done} face(s).");
    }

    // Occurrences stored before identities existed get their identity here: each photo's runs are
    // replayed oldest first and every face is matched against what earlier runs assigned to that
    // photo, so cross-version duplicates of one physical face collapse into one identity and
    // settled review states follow it.
    private async Task AssignFaceIdentitiesAsync(CancellationToken cancellationToken)
    {
        var pending = await database.FaceOccurrences
            .Where(occurrence => occurrence.IdentityId == null)
            .OrderBy(occurrence => occurrence.AssetId)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0) return;

        var runCompletionById = await database.PhotoAnalysisRuns
            .ToDictionaryAsync(run => run.Id, run => run.CompletedAtUtc, cancellationToken);
        var inheritedCount = 0;
        var createdCount = 0;
        foreach (var perAsset in pending.GroupBy(occurrence => occurrence.AssetId))
        {
            var assigned = (await database.FaceOccurrences
                .Where(occurrence => occurrence.AssetId == perAsset.Key && occurrence.IdentityId != null)
                .ToListAsync(cancellationToken))
                .Select(occurrence => new FaceForIdentityMatching(occurrence.Id, occurrence.RunId, occurrence.IdentityId, occurrence.Embedding))
                .ToList();

            foreach (var runGroup in perAsset
                         .GroupBy(occurrence => occurrence.RunId)
                         .OrderBy(group => runCompletionById.TryGetValue(group.Key, out var completed) ? completed : DateTime.MaxValue))
            {
                var newcomers = runGroup
                    .Select(occurrence => new FaceForIdentityMatching(occurrence.Id, occurrence.RunId, null, occurrence.Embedding))
                    .ToList();
                var inherited = FaceIdentityMatcher.MatchAgainstAssigned(newcomers, assigned);
                foreach (var occurrence in runGroup)
                {
                    if (inherited.TryGetValue(occurrence.Id, out var identityId)) inheritedCount++;
                    else
                    {
                        identityId = Guid.NewGuid().ToString("N");
                        database.FaceIdentities.Add(new FaceIdentity { Id = identityId, AssetId = perAsset.Key, CreatedAtUtc = DateTime.UtcNow });
                        createdCount++;
                    }
                    occurrence.IdentityId = identityId;
                }
                assigned.AddRange(runGroup.Select(occurrence =>
                    new FaceForIdentityMatching(occurrence.Id, occurrence.RunId, occurrence.IdentityId, occurrence.Embedding)));
            }
            await database.SaveChangesAsync(cancellationToken);
        }
        Console.WriteLine($"[face-migration] Assigned face identities: {inheritedCount} inherited, {createdCount} created.");
    }

    // Re-detections stored before identities existed left proposals open on faces a reviewer had
    // already disposed of on an earlier occurrence; nothing ranks a settled identity anymore, so
    // those proposals are retired instead of sitting in review forever.
    private async Task SupersedeSettledIdentityProposalsAsync(CancellationToken cancellationToken)
    {
        // A List: EF reliably turns List.Contains into SQL; an interface-typed set is not guaranteed to translate.
        var settledIdentityIds = (await FaceIdentityState.SettledIdsAsync(database, cancellationToken)).ToList();
        if (settledIdentityIds.Count == 0) return;
        var stale = await (
            from candidate in database.PhotoAnalysisCandidates
            join occurrence in database.FaceOccurrences on candidate.SubjectFaceOccurrenceId equals occurrence.Id
            where candidate.Kind == PhotoAnalysisCandidateKind.Person
                && candidate.SupersededAtUtc == null
                && occurrence.IdentityId != null && settledIdentityIds.Contains(occurrence.IdentityId)
                && !database.PhotoAnalysisReviewDecisions.Any(decision => decision.CandidateId == candidate.Id)
            select candidate).ToListAsync(cancellationToken);
        if (stale.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var candidate in stale) candidate.SupersededAtUtc = now;
        await database.SaveChangesAsync(cancellationToken);
        Console.WriteLine($"[face-migration] Superseded {stale.Count} proposal(s) on already-settled face identities.");
    }

    // Detection-version requeue uses EnsurePending semantics: a Done AnalyzeFaces job is the
    // current run row, not history, so it is reset rather than duplicated (unique index).
    // Outdated means no run of the current version at all - an older run alone is history.
    private async Task RequeueOutdatedDetectionsAsync(CancellationToken cancellationToken)
    {
        var currentVersionAssetIds = database.PhotoAnalysisRuns
            .Where(run => run.PipelineVersion == FaceAnalysisPipeline.CurrentDetectionVersion)
            .Select(run => run.AssetId);
        var outdatedAssetIds = await database.PhotoAnalysisRuns
            .Where(run => run.PipelineVersion.StartsWith("face-analysis/"))
            .Select(run => run.AssetId)
            .Except(currentVersionAssetIds)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (outdatedAssetIds.Count == 0) return;

        var assets = await database.Assets.ToListAsync(cancellationToken);
        var canonicalAssetIds = assets
            .GroupBy(asset => asset.ContentSha256 ?? asset.Id)
            .Select(group => group.OrderBy(asset => asset.UploadedAtUtc).ThenBy(asset => asset.Id).First().Id)
            .ToHashSet();
        var requeued = 0;
        foreach (var assetId in outdatedAssetIds.Where(canonicalAssetIds.Contains))
        {
            if (await ProcessingQueue.EnsurePendingAsync(database, assetId, JobKind.AnalyzeFaces, cancellationToken)) requeued++;
        }
        await database.SaveChangesAsync(cancellationToken);
        if (requeued > 0) Console.WriteLine($"[face-migration] Requeued detection for {requeued} photo(s) analysed by an older pipeline version.");
    }
}
