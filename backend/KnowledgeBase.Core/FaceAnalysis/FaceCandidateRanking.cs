using System.Text.Json;
using KnowledgeBase.Core.Persistence;

namespace KnowledgeBase.Core.FaceAnalysis;

/// <summary>One confirmed example of a person's face, as the ranking compares against it.</summary>
public sealed record PersonReferenceEmbedding(string PersonId, string PersonName, string FaceOccurrenceId, float[] Embedding);

// Shared by the detection run and the later re-score: both turn one face plus the current
// reference set into the same candidate rows.
public static class FaceCandidateRanking
{
    /// <summary>Below this a person is not worth offering: the reviewer would be choosing from noise.</summary>
    public const double MinimumScore = 0.25;

    /// <summary>A ceiling on how many people one face may propose, however many clear the floor.</summary>
    public const int MaximumCandidates = 20;

    /// <summary>How far a stored score must move before it is worth writing a new row for it.</summary>
    public const double ScoreTolerance = 0.03;

    // Measured against what is stored, never against the previous calculation, so a slow drift of
    // one percent at a time still crosses the tolerance instead of being ignored forever.
    public static bool MovedEnough(double storedScore, double freshScore) =>
        Math.Abs(freshScore - storedScore) >= ScoreTolerance;

    public static List<PhotoAnalysisCandidate> For(
        string runId,
        FaceOccurrence occurrence,
        IReadOnlyList<PersonReferenceEmbedding> references,
        DateTime createdAtUtc)
    {
        var ranked = references
            .GroupBy(reference => new { reference.PersonId, reference.PersonName })
            .Select(group => new { group.Key.PersonId, group.Key.PersonName, Best = group.OrderByDescending(reference => CosineSimilarity(occurrence.Embedding, reference.Embedding)).First() })
            .Select(item => new { item.PersonId, item.PersonName, Score = CosineSimilarity(occurrence.Embedding, item.Best.Embedding), item.Best.FaceOccurrenceId })
            .OrderByDescending(item => item.Score)
            // The best guess is kept even below the floor: dropping it would take the face itself out
            // of review, and a weak proposal is still the thing the reviewer answers about.
            .Where((item, index) => index == 0 || item.Score >= MinimumScore)
            .Take(MaximumCandidates).ToList();

        if (ranked.Count == 0)
        {
            return
            [
                Create(runId, occurrence, 1, 0, null, "Unknown face — add a person, then correct this candidate.",
                    JsonSerializer.Serialize(new { detectionScore = occurrence.DetectionScore, referenceCount = 0 }), createdAtUtc),
            ];
        }

        return ranked.Select((candidate, index) => Create(
            runId, occurrence, index + 1, candidate.Score, candidate.PersonId, candidate.PersonName,
            JsonSerializer.Serialize(new
            {
                metric = "cosine",
                referenceFaceId = candidate.FaceOccurrenceId,
                detectionScore = occurrence.DetectionScore,
                referenceCount = references.Count(reference => reference.PersonId == candidate.PersonId),
            }),
            createdAtUtc)).ToList();
    }

    // Rank records where this candidate stood when it was computed. It is not the current order:
    // a face's rows may come from several re-scores, so the live ranking is derived from Score.
    private static PhotoAnalysisCandidate Create(
        string runId, FaceOccurrence occurrence, int rank, double score, string? personId, string label, string signalsJson, DateTime createdAtUtc) => new()
    {
        Id = Guid.NewGuid().ToString("N"), RunId = runId, Kind = PhotoAnalysisCandidateKind.Person,
        SubjectAssetId = occurrence.AssetId, SubjectFaceOccurrenceId = occurrence.Id, ProposedTargetId = personId,
        ProposedLabel = label, Rank = rank, Score = score, SignalsJson = signalsJson, CreatedAtUtc = createdAtUtc,
    };

    private static double CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length != right.Length) throw new InvalidOperationException("Face embeddings from different models cannot be compared.");
        double dot = 0, leftLength = 0, rightLength = 0;
        for (var index = 0; index < left.Length; index++) { dot += left[index] * right[index]; leftLength += left[index] * left[index]; rightLength += right[index] * right[index]; }
        return leftLength == 0 || rightLength == 0 ? 0 : dot / Math.Sqrt(leftLength * rightLength);
    }
}
