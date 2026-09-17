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
            select new { PersonId = person.Id, person.Name, FaceOccurrenceId = occurrence.Id, occurrence.Embedding }).ToListAsync(cancellationToken);

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

            var ranked = references
                .GroupBy(reference => new { reference.PersonId, reference.Name })
                .Select(group => new { group.Key.PersonId, group.Key.Name, Best = group.OrderByDescending(reference => CosineSimilarity(face.Embedding, reference.Embedding)).First() })
                .Select(item => new { item.PersonId, item.Name, Score = CosineSimilarity(face.Embedding, item.Best.Embedding), item.Best.FaceOccurrenceId })
                .OrderByDescending(item => item.Score).Take(5).ToList();

            if (ranked.Count == 0)
            {
                database.PhotoAnalysisCandidates.Add(CreateCandidate(occurrence, 1, 0, null, "Unknown face — add a person, then correct this candidate.", JsonSerializer.Serialize(new { detectionScore = face.DetectionScore, referenceCount = 0 })));
                continue;
            }

            for (var index = 0; index < ranked.Count; index++)
            {
                var candidate = ranked[index];
                database.PhotoAnalysisCandidates.Add(CreateCandidate(occurrence, index + 1, candidate.Score, candidate.PersonId, candidate.Name, JsonSerializer.Serialize(new { metric = "cosine", referenceFaceId = candidate.FaceOccurrenceId, detectionScore = face.DetectionScore, referenceCount = references.Count(reference => reference.PersonId == candidate.PersonId) })));
            }
        }

        return null;
    }

    private static PhotoAnalysisCandidate CreateCandidate(FaceOccurrence occurrence, int rank, double score, string? personId, string label, string signalsJson) => new()
    {
        Id = Guid.NewGuid().ToString("N"), RunId = occurrence.RunId, Kind = PhotoAnalysisCandidateKind.Person,
        SubjectAssetId = occurrence.AssetId, SubjectFaceOccurrenceId = occurrence.Id, ProposedTargetId = personId,
        ProposedLabel = label, Rank = rank, Score = score, SignalsJson = signalsJson, CreatedAtUtc = occurrence.CreatedAtUtc
    };

    private static double CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length != right.Length) throw new InvalidOperationException("Face embeddings from different models cannot be compared.");
        double dot = 0, leftLength = 0, rightLength = 0;
        for (var index = 0; index < left.Length; index++) { dot += left[index] * right[index]; leftLength += left[index] * left[index]; rightLength += right[index] * right[index]; }
        return leftLength == 0 || rightLength == 0 ? 0 : dot / Math.Sqrt(leftLength * rightLength);
    }
}
