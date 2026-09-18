using System.Text.Json;
using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
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
        // put it up for review twice. Re-ranking against new references is RescoreFaces' job. Only
        // faces from this pipeline version count, so a requeue after a detection fix detects again.
        if (await (from occurrence in database.FaceOccurrences
                   join earlierRun in database.PhotoAnalysisRuns on occurrence.RunId equals earlierRun.Id
                   where occurrence.AssetId == asset.Id && earlierRun.PipelineVersion == PipelineVersion
                   select occurrence).AnyAsync(cancellationToken))
            throw new SkippableContentException("This image already has face occurrences from an earlier analysis run.");

        var faces = await analyzer.AnalyzeAsync(await reader.ReadBytesAsync(asset.StoredFileName, cancellationToken), cancellationToken);
        var now = DateTime.UtcNow;
        await database.PhotoAnalysisCandidates
            .Where(candidate => candidate.Kind == PhotoAnalysisCandidateKind.Person && candidate.SubjectAssetId == asset.Id
                && candidate.SupersededAtUtc == null
                && !database.PhotoAnalysisReviewDecisions.Any(decision => decision.CandidateId == candidate.Id))
            .ExecuteUpdateAsync(update => update.SetProperty(candidate => candidate.SupersededAtUtc, now), cancellationToken);
        var run = new PhotoAnalysisRun
        {
            Id = Guid.NewGuid().ToString("N"), AssetId = asset.Id, PipelineVersion = PipelineVersion,
            ModelKey = analyzer.ModelKey, ConfigurationHash = analyzer.ConfigurationHash, CompletedAtUtc = now
        };
        database.PhotoAnalysisRuns.Add(run);

        var references = await (
            from reference in database.PersonReferenceFaces
            join occurrence in database.FaceOccurrences on reference.FaceOccurrenceId equals occurrence.Id
            join person in database.People on reference.PersonId equals person.Id
            select new PersonReferenceEmbedding(person.Id, person.Name, occurrence.Id, occurrence.Embedding)).ToListAsync(cancellationToken);

        var earlierFaces = (await database.FaceOccurrences
            .Where(occurrence => occurrence.AssetId == asset.Id && occurrence.IdentityId != null)
            .ToListAsync(cancellationToken))
            .Select(occurrence => new FaceForIdentityMatching(occurrence.Id, occurrence.RunId, occurrence.IdentityId, occurrence.Embedding))
            .ToList();
        var settledIdentityIds = await FaceIdentityState.SettledIdsAsync(database, cancellationToken);

        var occurrences = faces.Select(face => new FaceOccurrence
        {
            Id = Guid.NewGuid().ToString("N"), RunId = run.Id, AssetId = asset.Id,
            X = face.X, Y = face.Y, Width = face.Width, Height = face.Height,
            DetectionScore = face.DetectionScore, LandmarksJson = JsonSerializer.Serialize(face.Landmarks),
            Embedding = face.Embedding, EmbeddingModelKey = analyzer.EmbeddingModelKey, CreatedAtUtc = now
        }).ToList();
        var inheritedIdentities = FaceIdentityMatcher.MatchAgainstAssigned(
            occurrences.Select(occurrence => new FaceForIdentityMatching(occurrence.Id, occurrence.RunId, null, occurrence.Embedding)).ToList(),
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
            // A settled identity is a face the reviewer already disposed of (a reference or a
            // rejection on any of its occurrences): a re-detection of it is stored as evidence
            // but never reopened for review.
            if (!settledIdentityIds.Contains(occurrence.IdentityId))
                database.PhotoAnalysisCandidates.AddRange(FaceCandidateRanking.For(run.Id, occurrence, references, now));
        }

        return null;
    }
}
