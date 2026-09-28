namespace KnowledgeBase.Core.SceneAnalysis;

public sealed record LocationReferenceEmbedding(
    string LocationId,
    string LocationName,
    string VisualEmbeddingId,
    float[] Embedding);

public sealed record SceneClusteringScene(
    string IdentityId,
    float[] Embedding,
    double? Latitude,
    double? Longitude);

public sealed record SceneClusteringResult(
    IReadOnlyList<SceneClusteringLocationJoin> LocationJoins,
    IReadOnlyList<SceneClusteringGroup> AnonymousClusters,
    IReadOnlyList<SceneClusteringExcludedGroup> ExcludedGroups,
    IReadOnlyList<string> UnsortedIdentityIds,
    IReadOnlyDictionary<string, double> OrderScores);

public sealed record SceneClusteringLocationJoin(
    string IdentityId,
    string LocationId,
    double Score,
    string BestReferenceEmbeddingId);

public sealed record SceneClusteringGroup(
    IReadOnlyList<string> IdentityIds,
    string? HintLocationId,
    double? HintScore);

public sealed record SceneClusteringExcludedGroup(
    string GroupId,
    IReadOnlyList<string> IdentityIds,
    string? HintLocationId,
    double? HintScore);

// Pure grouping math: join confirmed locations first, then excluded groups, then agglomerative
// average-linkage clustering over what is left, with GPS proximity bonus. No data access.
public static class SceneClustering
{
    private const double GpsProximityRadiusMeters = 100.0;
    private const double GpsProximityBonus = 0.10;

    public static SceneClusteringResult Run(
        IReadOnlyList<SceneClusteringScene> scenes,
        IReadOnlyList<LocationReferenceEmbedding> references,
        SceneIdentityReviewStates states,
        SceneClusteringOptions thresholds)
    {
        var locationSets = references
            .GroupBy(reference => reference.LocationId)
            .Select(group => BuildLocationReferenceSet(group.Key, group.ToList()))
            .ToList();

        var negatives = states.NegativeLocationIds;
        var orderScores = new Dictionary<string, double>();
        var unsorted = new List<string>();

        var excludedMembers = new Dictionary<string, List<SceneClusteringScene>>();
        var candidates = new List<SceneClusteringScene>();
        foreach (var scene in scenes)
        {
            if (states.PinnedUnsortedIds.Contains(scene.IdentityId))
            {
                unsorted.Add(scene.IdentityId);
            }
            else if (states.ExcludedGroupIds.TryGetValue(scene.IdentityId, out var groupId))
            {
                if (!excludedMembers.TryGetValue(groupId, out var members))
                    excludedMembers[groupId] = members = [];
                members.Add(scene);
            }
            else
            {
                candidates.Add(scene);
            }
        }

        var explicitExcluded = excludedMembers.ToDictionary(
            pair => pair.Key,
            pair => (Centroid: ComputeCentroid(pair.Value.Select(m => m.Embedding).ToList()), Members: pair.Value.ToList()));

        var joins = new List<SceneClusteringLocationJoin>();
        var remaining = new List<SceneClusteringScene>();
        foreach (var scene in candidates)
        {
            var sceneNegatives = negatives.GetValueOrDefault(scene.IdentityId);
            string? bestLocation = null;
            var bestScore = double.NegativeInfinity;
            var bestReference = string.Empty;
            foreach (var location in locationSets)
            {
                if (sceneNegatives?.Contains(location.LocationId) == true) continue;
                var score = LocationScore(scene.Embedding, location, out var referenceId);
                if (score <= bestScore) continue;
                bestScore = score;
                bestLocation = location.LocationId;
                bestReference = referenceId;
            }
            if (bestLocation is not null && bestScore >= thresholds.LocationJoinThreshold)
            {
                joins.Add(new SceneClusteringLocationJoin(scene.IdentityId, bestLocation, bestScore, bestReference));
                orderScores[scene.IdentityId] = bestScore;
            }
            else
            {
                remaining.Add(scene);
            }
        }

        var freeScenes = new List<SceneClusteringScene>();
        foreach (var scene in remaining)
        {
            string? bestGroup = null;
            var bestScore = double.NegativeInfinity;
            foreach (var (groupId, (groupCentroid, _)) in explicitExcluded)
            {
                var score = CosineSimilarity(scene.Embedding, groupCentroid);
                if (score <= bestScore) continue;
                bestScore = score;
                bestGroup = groupId;
            }
            if (bestGroup is not null && bestScore >= thresholds.LocationJoinThreshold)
            {
                excludedMembers[bestGroup].Add(scene);
            }
            else
            {
                freeScenes.Add(scene);
            }
        }

        var anonymousClusters = new List<SceneClusteringGroup>();
        foreach (var cluster in AverageLinkageClusters(freeScenes, thresholds.ClusterThreshold))
        {
            if (cluster.Count == 1)
            {
                unsorted.Add(cluster[0].IdentityId);
                continue;
            }
            SetAverageOrderScores(cluster, orderScores);
            var (hintLocationId, hintScore) = Hint(cluster, locationSets, negatives, thresholds);
            anonymousClusters.Add(new SceneClusteringGroup(
                cluster.Select(member => member.IdentityId).ToList(),
                hintLocationId,
                hintScore));
        }

        var excludedGroups = new List<SceneClusteringExcludedGroup>();
        foreach (var (groupId, members) in excludedMembers)
        {
            SetAverageOrderScores(members, orderScores);
            var (hintLocationId, hintScore) = Hint(members, locationSets, negatives, thresholds);
            excludedGroups.Add(new SceneClusteringExcludedGroup(
                groupId,
                members.Select(member => member.IdentityId).ToList(),
                hintLocationId,
                hintScore));
        }

        foreach (var identityId in unsorted) orderScores[identityId] = 0;

        return new SceneClusteringResult(joins, anonymousClusters, excludedGroups, unsorted, orderScores);
    }

    public static double SceneSimilarity(SceneClusteringScene a, SceneClusteringScene b)
    {
        var cosine = CosineSimilarity(a.Embedding, b.Embedding);

        if (a.Latitude.HasValue && a.Longitude.HasValue
            && b.Latitude.HasValue && b.Longitude.HasValue)
        {
            var distanceMeters = HaversineDistance(
                a.Latitude.Value, a.Longitude.Value,
                b.Latitude.Value, b.Longitude.Value);
            if (distanceMeters <= GpsProximityRadiusMeters)
                cosine += GpsProximityBonus;
        }

        return cosine;
    }

    public static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371000.0;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);
        var c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        return r * c;
    }

    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0;
        double normA = 0;
        double normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom == 0 ? 0 : dot / denom;
    }

    private sealed record LocationReferenceSet(
        string LocationId,
        float[] Centroid,
        IReadOnlyList<LocationReferenceEmbedding> InlierReferences);

    private static LocationReferenceSet BuildLocationReferenceSet(
        string locationId, IReadOnlyList<LocationReferenceEmbedding> references)
    {
        if (references.Count <= 2)
        {
            var centroid = ComputeCentroid(references.Select(r => r.Embedding).ToList());
            return new LocationReferenceSet(locationId, centroid, references);
        }

        var inliers = new List<LocationReferenceEmbedding>();
        for (var i = 0; i < references.Count; i++)
        {
            var sumSim = 0.0;
            for (var j = 0; j < references.Count; j++)
            {
                if (i != j)
                    sumSim += CosineSimilarity(references[i].Embedding, references[j].Embedding);
            }
            var avgSim = sumSim / (references.Count - 1);
            if (avgSim >= 0.15)
            {
                inliers.Add(references[i]);
            }
        }

        var effectiveReferences = inliers.Count >= 2 ? inliers : references;
        var effectiveCentroid = ComputeCentroid(effectiveReferences.Select(r => r.Embedding).ToList());
        return new LocationReferenceSet(locationId, effectiveCentroid, effectiveReferences);
    }

    private static double LocationScore(float[] embedding, LocationReferenceSet location, out string bestReferenceId)
    {
        var centroidScore = CosineSimilarity(embedding, location.Centroid);
        var bestRefScore = double.NegativeInfinity;
        bestReferenceId = string.Empty;
        foreach (var reference in location.InlierReferences)
        {
            var refScore = CosineSimilarity(embedding, reference.Embedding);
            if (refScore > bestRefScore)
            {
                bestRefScore = refScore;
                bestReferenceId = reference.VisualEmbeddingId;
            }
        }
        return centroidScore;
    }

    private static float[] ComputeCentroid(IReadOnlyList<float[]> embeddings)
    {
        if (embeddings.Count == 0) return [];
        if (embeddings.Count == 1) return Normalize(embeddings[0]);

        var length = embeddings[0].Length;
        var sum = new double[length];
        foreach (var emb in embeddings)
        {
            var norm = Normalize(emb);
            for (var i = 0; i < length; i++) sum[i] += norm[i];
        }
        var centroid = new float[length];
        for (var i = 0; i < length; i++) centroid[i] = (float)sum[i];
        return Normalize(centroid);
    }

    private static float[] Normalize(float[] vector)
    {
        double sumSq = 0;
        for (var i = 0; i < vector.Length; i++) sumSq += vector[i] * vector[i];
        var norm = Math.Sqrt(sumSq);
        if (norm == 0) return (float[])vector.Clone();
        var result = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++) result[i] = (float)(vector[i] / norm);
        return result;
    }

    private static void SetAverageOrderScores(IReadOnlyList<SceneClusteringScene> members, Dictionary<string, double> orderScores)
    {
        for (var index = 0; index < members.Count; index++)
        {
            var total = 0.0;
            for (var other = 0; other < members.Count; other++)
                if (other != index) total += SceneSimilarity(members[index], members[other]);
            orderScores[members[index].IdentityId] = members.Count > 1 ? total / (members.Count - 1) : 0;
        }
    }

    private static (string? LocationId, double? Score) Hint(
        IReadOnlyList<SceneClusteringScene> members,
        IReadOnlyList<LocationReferenceSet> locationSets,
        IReadOnlyDictionary<string, IReadOnlySet<string>> negatives,
        SceneClusteringOptions thresholds)
    {
        string? bestLocation = null;
        var bestScore = double.NegativeInfinity;
        foreach (var location in locationSets)
        {
            if (members.Any(member => negatives.GetValueOrDefault(member.IdentityId)?.Contains(location.LocationId) == true)) continue;
            var average = members.Average(member => CosineSimilarity(member.Embedding, location.Centroid));
            if (average <= bestScore) continue;
            bestScore = average;
            bestLocation = location.LocationId;
        }
        return bestLocation is not null && bestScore >= thresholds.HintThreshold && bestScore < thresholds.LocationJoinThreshold
            ? (bestLocation, bestScore)
            : (null, null);
    }

    private static List<List<SceneClusteringScene>> AverageLinkageClusters(
        IReadOnlyList<SceneClusteringScene> scenes,
        double clusterThreshold)
    {
        var count = scenes.Count;
        var members = scenes.Select(scene => new List<SceneClusteringScene> { scene }).ToList();
        var sums = new List<List<double>>(count);
        for (var index = 0; index < count; index++)
        {
            var row = new List<double>(count);
            for (var other = 0; other < count; other++)
                row.Add(SceneSimilarity(scenes[index], scenes[other]));
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
