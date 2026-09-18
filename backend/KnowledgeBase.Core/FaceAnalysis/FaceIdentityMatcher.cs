namespace KnowledgeBase.Core.FaceAnalysis;

/// <summary>A face occurrence reduced to what identity matching needs.</summary>
public sealed record FaceForIdentityMatching(string Id, string RunId, string? IdentityId, float[] Embedding);

// Decides whether a detected face is a re-detection of a face an earlier run already stored, so
// the new occurrence inherits its identity instead of reopening a settled subject for review.
// The comparison is embedding cosine, not box overlap: a re-detection crops nearly the same
// pixels, so the cosine survives a detector change or an orientation fix, which box geometry
// cannot promise. Pairs match one-to-one within a run pair, so a person appearing twice in one
// photo keeps two identities instead of collapsing into one.
public static class FaceIdentityMatcher
{
    /// <summary>Same-photo re-crops of one face score clearly above this and different faces on
    /// one photo clearly below; revisit together with the pending score calibration.</summary>
    public const double MinimumCosine = 0.5;

    // Best pairs first; each earlier occurrence absorbs at most one newcomer per run.
    public static IReadOnlyDictionary<string, string> MatchAgainstAssigned(
        IReadOnlyList<FaceForIdentityMatching> newcomers,
        IReadOnlyList<FaceForIdentityMatching> assigned)
    {
        var pairs = new List<(string NewcomerId, string TargetId, string IdentityId, double Score)>();
        foreach (var newcomer in newcomers)
        foreach (var target in assigned)
        {
            if (target.IdentityId is null || target.RunId == newcomer.RunId) continue;
            if (newcomer.Embedding.Length != target.Embedding.Length) continue;
            var score = FaceCandidateRanking.CosineSimilarity(newcomer.Embedding, target.Embedding);
            if (score >= MinimumCosine) pairs.Add((newcomer.Id, target.Id, target.IdentityId, score));
        }

        var inherited = new Dictionary<string, string>();
        var claimedTargets = new HashSet<string>();
        foreach (var (newcomerId, targetId, identityId, _) in pairs.OrderByDescending(pair => pair.Score))
        {
            if (inherited.ContainsKey(newcomerId) || !claimedTargets.Add(targetId)) continue;
            inherited[newcomerId] = identityId;
        }
        return inherited;
    }
}
