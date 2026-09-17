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
        // put it up for review twice. Re-ranking against new references is RescoreFaces' job.
        if (await database.FaceOccurrences.AnyAsync(occurrence => occurrence.AssetId == asset.Id, cancellationToken))
            throw new SkippableContentException("This image already has face occurrences from an earlier analysis run.");

        var faces = await analyzer.AnalyzeAsync(await reader.ReadBytesAsync(asset.StoredFileName, cancellationToken), cancellationToken);
        var now = DateTime.UtcNow;
        var run = new PhotoAnalysisRun
        {
            Id = Guid.NewGuid().ToString("N"), AssetId = asset.Id, PipelineVersion = "face-analysis/v1",
            ModelKey = analyzer.ModelKey, ConfigurationHash = analyzer.ConfigurationHash, CompletedAtUtc = now
        };
        database.PhotoAnalysisRuns.Add(run);

        var references = await (
            from reference in database.PersonReferenceFaces
            join occurrence in database.FaceOccurrences on reference.FaceOccurrenceId equals occurrence.Id
            join person in database.People on reference.PersonId equals person.Id
            select new PersonReferenceEmbedding(person.Id, person.Name, occurrence.Id, occurrence.Embedding)).ToListAsync(cancellationToken);

        foreach (var face in faces)
        {
            var occurrence = new FaceOccurrence
            {
                Id = Guid.NewGuid().ToString("N"), RunId = run.Id, AssetId = asset.Id,
                X = face.X, Y = face.Y, Width = face.Width, Height = face.Height,
                DetectionScore = face.DetectionScore, LandmarksJson = JsonSerializer.Serialize(face.Landmarks),
                Embedding = face.Embedding, CreatedAtUtc = now
            };
            database.FaceOccurrences.Add(occurrence);
            database.PhotoAnalysisCandidates.AddRange(FaceCandidateRanking.For(run.Id, occurrence, references, now));
        }

        return null;
    }
}
