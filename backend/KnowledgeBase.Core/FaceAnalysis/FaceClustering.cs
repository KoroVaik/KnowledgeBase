namespace KnowledgeBase.Core.FaceAnalysis;

/// <summary>One confirmed example of a person's face, as clustering compares against it.</summary>
public sealed record PersonReferenceEmbedding(string PersonId, string PersonName, string FaceOccurrenceId, float[] Embedding);

/// <summary>One open face reduced to what clustering needs; the identity is the review subject.</summary>
public sealed record FaceClusteringFace(string IdentityId, float[] Embedding);

/// <summary>Where every input face ended up. Each identity appears exactly once across the four
/// destinations; OrderScores carries the within-row display order (most similar first).</summary>
public sealed record FaceClusteringResult(
    IReadOnlyList<FaceClusteringPersonJoin> PersonJoins,
    IReadOnlyList<FaceClusteringGroup> AnonymousClusters,
    IReadOnlyList<FaceClusteringIgnoredGroup> IgnoredGroups,
    IReadOnlyList<string> UnsortedIdentityIds,
    IReadOnlyDictionary<string, double> OrderScores);

public sealed record FaceClusteringPersonJoin(string IdentityId, string PersonId, double Score, string BestReferenceFaceOccurrenceId);

public sealed record FaceClusteringGroup(IReadOnlyList<string> IdentityIds, string? HintPersonId, double? HintScore);

public sealed record FaceClusteringIgnoredGroup(string GroupId, IReadOnlyList<string> IdentityIds, string? HintPersonId, double? HintScore);

// Pure grouping math: join confirmed people first, then ignored groups, then agglomerative
// average-linkage clustering over what is left. No data access, so it is testable without a DB.
public static class FaceClustering
{
    public static FaceClusteringResult Run(
        IReadOnlyList<FaceClusteringFace> faces,
        IReadOnlyList<PersonReferenceEmbedding> references,
        FaceIdentityReviewStates states,
        FaceClusteringOptions thresholds)
    {
        var personIds = references.Select(reference => reference.PersonId).Distinct().ToList();
        var negatives = states.NegativePersonIds;
        var orderScores = new Dictionary<string, double>();
        var unsorted = new List<string>();

        var ignoredMembers = new Dictionary<string, List<FaceClusteringFace>>();
        var candidates = new List<FaceClusteringFace>();
        foreach (var face in faces)
        {
            if (states.PinnedUnsortedIds.Contains(face.IdentityId)) unsorted.Add(face.IdentityId);
            else if (states.IgnoredGroupIds.TryGetValue(face.IdentityId, out var groupId))
            {
                if (!ignoredMembers.TryGetValue(groupId, out var members)) ignoredMembers[groupId] = members = [];
                members.Add(face);
            }
            else candidates.Add(face);
        }
        // Joining compares against what the user filed into the group, not what earlier runs
        // joined to it, so a group cannot drift away from the faces the user actually ignored.
        var explicitIgnored = ignoredMembers.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());

        var joins = new List<FaceClusteringPersonJoin>();
        var remaining = new List<FaceClusteringFace>();
        foreach (var face in candidates)
        {
            var faceNegatives = negatives.GetValueOrDefault(face.IdentityId);
            string? bestPerson = null;
            var bestScore = double.NegativeInfinity;
            var bestReference = string.Empty;
            foreach (var personId in personIds)
            {
                if (faceNegatives?.Contains(personId) == true) continue;
                var score = BestPersonScore(face.Embedding, personId, references, out var referenceId);
                if (score <= bestScore) continue;
                bestScore = score;
                bestPerson = personId;
                bestReference = referenceId;
            }
            if (bestPerson is not null && bestScore >= thresholds.PersonJoinThreshold)
            {
                joins.Add(new FaceClusteringPersonJoin(face.IdentityId, bestPerson, bestScore, bestReference));
                orderScores[face.IdentityId] = bestScore;
            }
            else remaining.Add(face);
        }

        var freeFaces = new List<FaceClusteringFace>();
        foreach (var face in remaining)
        {
            string? bestGroup = null;
            var bestScore = double.NegativeInfinity;
            foreach (var (groupId, members) in explicitIgnored)
            foreach (var member in members)
            {
                var score = FaceEmbeddingMath.CosineSimilarity(face.Embedding, member.Embedding);
                if (score <= bestScore) continue;
                bestScore = score;
                bestGroup = groupId;
            }
            if (bestGroup is not null && bestScore >= thresholds.PersonJoinThreshold) ignoredMembers[bestGroup].Add(face);
            else freeFaces.Add(face);
        }

        var anonymousClusters = new List<FaceClusteringGroup>();
        foreach (var cluster in AverageLinkageClusters(freeFaces, thresholds.ClusterThreshold))
        {
            if (cluster.Count == 1)
            {
                unsorted.Add(cluster[0].IdentityId);
                continue;
            }
            SetAverageOrderScores(cluster, orderScores);
            var (hintPersonId, hintScore) = Hint(cluster, personIds, references, negatives, thresholds);
            anonymousClusters.Add(new FaceClusteringGroup(cluster.Select(member => member.IdentityId).ToList(), hintPersonId, hintScore));
        }

        var ignoredGroups = new List<FaceClusteringIgnoredGroup>();
        foreach (var (groupId, members) in ignoredMembers)
        {
            SetAverageOrderScores(members, orderScores);
            var (hintPersonId, hintScore) = Hint(members, personIds, references, negatives, thresholds);
            ignoredGroups.Add(new FaceClusteringIgnoredGroup(groupId, members.Select(member => member.IdentityId).ToList(), hintPersonId, hintScore));
        }

        foreach (var identityId in unsorted) orderScores[identityId] = 0;

        return new FaceClusteringResult(joins, anonymousClusters, ignoredGroups, unsorted, orderScores);
    }

    private static double BestPersonScore(float[] embedding, string personId, IReadOnlyList<PersonReferenceEmbedding> references, out string bestReferenceId)
    {
        var best = double.NegativeInfinity;
        bestReferenceId = string.Empty;
        foreach (var reference in references)
        {
            if (reference.PersonId != personId) continue;
            var score = FaceEmbeddingMath.CosineSimilarity(embedding, reference.Embedding);
            if (score <= best) continue;
            best = score;
            bestReferenceId = reference.FaceOccurrenceId;
        }
        return best;
    }

    private static void SetAverageOrderScores(IReadOnlyList<FaceClusteringFace> members, Dictionary<string, double> orderScores)
    {
        for (var index = 0; index < members.Count; index++)
        {
            var total = 0.0;
            for (var other = 0; other < members.Count; other++)
                if (other != index) total += FaceEmbeddingMath.CosineSimilarity(members[index].Embedding, members[other].Embedding);
            orderScores[members[index].IdentityId] = members.Count > 1 ? total / (members.Count - 1) : 0;
        }
    }

    // Shown only in the band below PersonJoinThreshold: at or above it the faces would have
    // joined the person outright, so a hint there would mean the join logic disagreed with itself.
    private static (string? PersonId, double? Score) Hint(
        IReadOnlyList<FaceClusteringFace> members,
        IReadOnlyList<string> personIds,
        IReadOnlyList<PersonReferenceEmbedding> references,
        IReadOnlyDictionary<string, IReadOnlySet<string>> negatives,
        FaceClusteringOptions thresholds)
    {
        string? bestPerson = null;
        var bestScore = double.NegativeInfinity;
        foreach (var personId in personIds)
        {
            // One member's negative vetoes the hint for the whole row: a face the reviewer already
            // deleted from that person must not vote for it.
            if (members.Any(member => negatives.GetValueOrDefault(member.IdentityId)?.Contains(personId) == true)) continue;
            var average = members.Average(member => BestPersonScore(member.Embedding, personId, references, out _));
            if (average <= bestScore) continue;
            bestScore = average;
            bestPerson = personId;
        }
        return bestPerson is not null && bestScore >= thresholds.HintThreshold && bestScore < thresholds.PersonJoinThreshold
            ? (bestPerson, bestScore)
            : (null, null);
    }

    // Average linkage on cosine similarity, merged while the linkage reaches the threshold.
    // Pairwise cosine sums are kept in a matrix and merged rows updated incrementally, so no
    // pair is ever recomputed. Ties break by the lowest index pair, so the same faces cluster
    // the same way on every run.
    private static List<List<FaceClusteringFace>> AverageLinkageClusters(
        IReadOnlyList<FaceClusteringFace> faces,
        double clusterThreshold)
    {
        var count = faces.Count;
        var members = faces.Select(face => new List<FaceClusteringFace> { face }).ToList();
        var sums = new List<List<double>>(count);
        for (var index = 0; index < count; index++)
        {
            var row = new List<double>(count);
            for (var other = 0; other < count; other++)
                row.Add(FaceEmbeddingMath.CosineSimilarity(faces[index].Embedding, faces[other].Embedding));
            sums.Add(row);
        }

        while (members.Count > 1)
        {
            var bestA = -1;
            var bestB = -1;
            var bestLinkage = double.NegativeInfinity;
            for (var a = 0; a < members.Count; a++)
            for (var b = a + 1; b < members.Count; b++)
            {
                var linkage = sums[a][b] / (members[a].Count * members[b].Count);
                if (linkage <= bestLinkage) continue;
                bestLinkage = linkage;
                bestA = a;
                bestB = b;
            }
            if (bestA < 0 || bestLinkage < clusterThreshold) break;

            for (var other = 0; other < members.Count; other++)
            {
                if (other == bestA || other == bestB) continue;
                sums[bestA][other] += sums[bestB][other];
                sums[other][bestA] = sums[bestA][other];
            }
            members[bestA].AddRange(members[bestB]);
            members.RemoveAt(bestB);
            sums.RemoveAt(bestB);
            foreach (var row in sums) row.RemoveAt(bestB);
        }

        return members;
    }
}
