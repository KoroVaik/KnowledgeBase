using System.Text.Json;
using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.Pipeline.FaceAnalysis;
using KnowledgeBase.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Worker.FaceAnalysis;

public sealed class FaceAnalysisHandler(KnowledgeBaseDbContext database, IAssetContentReader reader, IFaceAnalyzer analyzer) : IPipelineHandler
{
    private const string PipelineVersion = FaceAnalysisPipeline.CurrentDetectionVersion;

    public JobKind Kind => JobKind.AnalyzeFaces;

    public bool RequiresContentAnalyzer => false;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var assetId = job.AssetId ?? throw new InvalidOperationException($"Job {job.Id} is a face-analysis job with no asset.");
        var asset = await database.Assets.SingleOrDefaultAsync(item => item.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Asset {assetId} no longer exists.");
        if (ProcessableContent.Classify(asset.ContentType, asset.OriginalFileName) is not ContentKind.Image)
            throw new SkippableContentException("Face analysis only applies to image assets.");
        // Queued before any skip: the skip path saves it too, and a ClusterFaces job that ran while
        // this one was pending deferred to it, so the last detection job must always leave one behind.
        await ClusterFacesQueue.EnqueueAsync(database, cancellationToken);
        if (asset.ContentSha256 is not null)
        {
            var canonicalAssetId = await database.Assets
                .Where(item => item.ContentSha256 == asset.ContentSha256)
                .OrderBy(item => item.UploadedAtUtc).ThenBy(item => item.Id)
                .Select(item => item.Id)
                .FirstAsync(cancellationToken);
            if (canonicalAssetId != asset.Id)
                throw new SkippableContentException("This image is an exact duplicate of an earlier archive asset.");
        }

        // A job left Running by a stopped worker is requeued on the next start, and detection has no
        // memory of its own: running it twice would store every face of this photo a second time and
        // put it up for review twice. Only faces from this pipeline version count, so a requeue after
        // a detection fix detects again.
        if (await (from occurrence in database.FaceOccurrences
                   join earlierRun in database.PhotoAnalysisRuns on occurrence.RunId equals earlierRun.Id
                   where occurrence.AssetId == asset.Id && earlierRun.PipelineVersion == PipelineVersion
                   select occurrence).AnyAsync(cancellationToken))
            throw new SkippableContentException("This image already has face occurrences from an earlier analysis run.");

        var faces = await analyzer.AnalyzeAsync(await reader.ReadBytesAsync(asset.StoredFileName, cancellationToken), cancellationToken);
        var now = DateTime.UtcNow;
        var run = new PhotoAnalysisRun
        {
            Id = Guid.NewGuid().ToString("N"), AssetId = asset.Id, PipelineVersion = PipelineVersion,
            ModelKey = analyzer.ModelKey, ConfigurationHash = analyzer.ConfigurationHash, CompletedAtUtc = now
        };
        database.PhotoAnalysisRuns.Add(run);

        var earlierFaces = (await database.FaceOccurrences
            .Where(occurrence => occurrence.AssetId == asset.Id && occurrence.IdentityId != null && !occurrence.IsPartial)
            .ToListAsync(cancellationToken))
            .Select(occurrence => new FaceForIdentityMatching(occurrence.Id, occurrence.RunId, occurrence.IdentityId, occurrence.Embedding))
            .ToList();

        var occurrences = faces.Select(face => new FaceOccurrence
        {
            Id = Guid.NewGuid().ToString("N"), RunId = run.Id, AssetId = asset.Id,
            X = face.X, Y = face.Y, Width = face.Width, Height = face.Height,
            DetectionScore = face.DetectionScore, IsPartial = face.IsPartial, LandmarksJson = JsonSerializer.Serialize(face.Landmarks),
            Embedding = face.Embedding, EmbeddingModelKey = analyzer.EmbeddingModelKey, CreatedAtUtc = now
        }).ToList();
        var inheritedIdentities = FaceIdentityMatcher.MatchAgainstAssigned(
            occurrences.Where(occurrence => !occurrence.IsPartial)
                .Select(occurrence => new FaceForIdentityMatching(occurrence.Id, occurrence.RunId, null, occurrence.Embedding)).ToList(),
            earlierFaces);

        foreach (var occurrence in occurrences)
        {
            if (!inheritedIdentities.TryGetValue(occurrence.Id, out var identityId))
            {
                identityId = Guid.NewGuid().ToString("N");
                database.FaceIdentities.Add(new FaceIdentity { Id = identityId, AssetId = asset.Id, CreatedAtUtc = now });
            }
            occurrence.IdentityId = identityId;
            database.FaceOccurrences.Add(occurrence);
        }

        return null;
    }
}
