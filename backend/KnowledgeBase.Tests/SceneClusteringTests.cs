using KnowledgeBase.Core.SceneAnalysis;
using Xunit;

namespace KnowledgeBase.Tests;

public sealed class SceneClusteringTests
{
    private static readonly SceneClusteringOptions DefaultOptions = new();
    private static readonly SceneIdentityReviewStates EmptyStates = new()
    {
        SettledIds = new HashSet<string>(),
        ExcludedGroupIds = new Dictionary<string, string>(),
        PinnedUnsortedIds = new HashSet<string>(),
        NegativeLocationIds = new Dictionary<string, IReadOnlySet<string>>()
    };

    [Fact]
    public void CandidateJoinsLocationWhenMatchingSingleReference()
    {
        var refEmb = UnitVector(0);
        var matchingScene = UnitVector(0);
        var otherScene = UnitVector(1);

        var references = new List<LocationReferenceEmbedding>
        {
            new("loc-1", "Location 1", "ref-1", refEmb),
        };
        var scenes = new List<SceneClusteringScene>
        {
            new("scene-match", matchingScene, null, null),
            new("scene-other", otherScene, null, null),
        };

        var result = SceneClustering.Run(scenes, references, EmptyStates, DefaultOptions);

        var join = Assert.Single(result.LocationJoins);
        Assert.Equal("scene-match", join.IdentityId);
        Assert.Equal("loc-1", join.LocationId);
        Assert.Equal("ref-1", join.BestReferenceEmbeddingId);
        Assert.True(join.Score >= 0.99);

        Assert.Contains("scene-other", result.UnsortedIdentityIds);
    }

    [Fact]
    public void OutlierReferenceDoesNotPoisonLocationCentroid()
    {
        var ref1 = Vector(1.0f, 0.1f, 0.0f);
        var ref2 = Vector(0.95f, 0.15f, 0.0f);
        var ref3 = Vector(0.98f, -0.05f, 0.0f);
        var refOutlier = Vector(0.0f, 0.0f, 1.0f);

        var references = new List<LocationReferenceEmbedding>
        {
            new("loc-1", "Park", "ref-1", ref1),
            new("loc-1", "Park", "ref-2", ref2),
            new("loc-1", "Park", "ref-3", ref3),
            new("loc-1", "Park", "ref-bad", refOutlier),
        };

        var realCandidate = Vector(0.92f, 0.1f, 0.0f);
        var otherCandidate = Vector(0.05f, 0.0f, 0.95f);

        var scenes = new List<SceneClusteringScene>
        {
            new("scene-real", realCandidate, null, null),
            new("scene-other", otherCandidate, null, null),
        };

        var result = SceneClustering.Run(scenes, references, EmptyStates, DefaultOptions);

        var join = Assert.Single(result.LocationJoins);
        Assert.Equal("scene-real", join.IdentityId);
        Assert.Equal("loc-1", join.LocationId);

        Assert.Contains("scene-other", result.UnsortedIdentityIds);
    }

    [Fact]
    public void UnmatchedScenesClusterTogetherAnonymously()
    {
        var refEmb = UnitVector(0);
        var references = new List<LocationReferenceEmbedding>
        {
            new("loc-1", "Location 1", "ref-1", refEmb),
        };

        var scene1 = Vector(0.0f, 0.98f, 0.05f);
        var scene2 = Vector(0.0f, 0.95f, -0.1f);

        var scenes = new List<SceneClusteringScene>
        {
            new("unknown-1", scene1, null, null),
            new("unknown-2", scene2, null, null),
        };

        var result = SceneClustering.Run(scenes, references, EmptyStates, DefaultOptions);

        Assert.Empty(result.LocationJoins);
        var cluster = Assert.Single(result.AnonymousClusters);
        Assert.Contains("unknown-1", cluster.IdentityIds);
        Assert.Contains("unknown-2", cluster.IdentityIds);
        Assert.Empty(result.UnsortedIdentityIds);
    }

    [Fact]
    public void SingletonScenesGoToUnsorted()
    {
        var scene1 = UnitVector(0);
        var scene2 = UnitVector(1);
        var scene3 = UnitVector(2);

        var scenes = new List<SceneClusteringScene>
        {
            new("scene-1", scene1, null, null),
            new("scene-2", scene2, null, null),
            new("scene-3", scene3, null, null),
        };

        var result = SceneClustering.Run(scenes, [], EmptyStates, DefaultOptions);

        Assert.Empty(result.AnonymousClusters);
        Assert.Equal(3, result.UnsortedIdentityIds.Count);
        Assert.Contains("scene-1", result.UnsortedIdentityIds);
        Assert.Contains("scene-2", result.UnsortedIdentityIds);
        Assert.Contains("scene-3", result.UnsortedIdentityIds);
    }

    [Fact]
    public void LooksLikeHintAppearsBetweenHintAndJoinThreshold()
    {
        // Join threshold is 0.70, hint threshold is 0.50
        var locRef = UnitVector(0);
        var references = new List<LocationReferenceEmbedding>
        {
            new("loc-1", "Castle", "ref-1", locRef),
        };

        // Cosine with axis 0 will be ~0.60: above 0.50 (hint) but below 0.70 (join)
        // Normalized vector: (0.6, 0.8, 0.0) -> dot with (1, 0, 0) = 0.60
        var s1 = Vector(0.6f, 0.79f, 0.05f);
        var s2 = Vector(0.6f, 0.80f, -0.05f);

        var scenes = new List<SceneClusteringScene>
        {
            new("scene-1", s1, null, null),
            new("scene-2", s2, null, null),
        };

        var result = SceneClustering.Run(scenes, references, EmptyStates, DefaultOptions);

        Assert.Empty(result.LocationJoins);
        var cluster = Assert.Single(result.AnonymousClusters);
        Assert.Equal("loc-1", cluster.HintLocationId);
        Assert.NotNull(cluster.HintScore);
        Assert.True(cluster.HintScore >= 0.50 && cluster.HintScore < 0.70);
    }

    [Fact]
    public void NegativeLocationIsNeverJoinedOrHinted()
    {
        var refEmb = UnitVector(0);
        var matchingScene = UnitVector(0);

        var references = new List<LocationReferenceEmbedding>
        {
            new("loc-1", "Location 1", "ref-1", refEmb),
        };
        var scenes = new List<SceneClusteringScene>
        {
            new("scene-match", matchingScene, null, null),
        };

        var statesWithNegative = new SceneIdentityReviewStates
        {
            SettledIds = new HashSet<string>(),
            ExcludedGroupIds = new Dictionary<string, string>(),
            PinnedUnsortedIds = new HashSet<string>(),
            NegativeLocationIds = new Dictionary<string, IReadOnlySet<string>>
            {
                ["scene-match"] = new HashSet<string> { "loc-1" }
            }
        };

        var result = SceneClustering.Run(scenes, references, statesWithNegative, DefaultOptions);

        Assert.Empty(result.LocationJoins);
        Assert.Contains("scene-match", result.UnsortedIdentityIds);
    }

    [Fact]
    public void GpsProximityBonusEnablesClusteringAcrossBorderlineCosine()
    {
        // ClusterThreshold is 0.65.
        // Let's create two vectors with cosine ~0.60.
        // Without GPS: cosine 0.60 < 0.65 -> they do NOT cluster together (both go to unsorted).
        // With GPS <= 100m: similarity = 0.60 + 0.10 = 0.70 >= 0.65 -> they cluster together!

        // Normalize: (0.6, 0.8) and (1.0, 0.0) -> dot = 0.60
        var v1 = Vector(1.0f, 0.0f);
        var v2 = Vector(0.6f, 0.8f);

        // Same location: Kyiv coordinates (50.4501, 30.5234)
        var lat = 50.450100;
        var lon = 30.523400;
        // ~50 meters away: ~0.00045 degrees lat
        var latClose = 50.450450;
        var lonClose = 30.523400;

        var scenesWithoutGps = new List<SceneClusteringScene>
        {
            new("s1", v1, null, null),
            new("s2", v2, null, null),
        };

        var resultNoGps = SceneClustering.Run(scenesWithoutGps, [], EmptyStates, DefaultOptions);
        Assert.Empty(resultNoGps.AnonymousClusters);
        Assert.Equal(2, resultNoGps.UnsortedIdentityIds.Count);

        var scenesWithGps = new List<SceneClusteringScene>
        {
            new("s1", v1, lat, lon),
            new("s2", v2, latClose, lonClose),
        };

        var resultWithGps = SceneClustering.Run(scenesWithGps, [], EmptyStates, DefaultOptions);
        var cluster = Assert.Single(resultWithGps.AnonymousClusters);
        Assert.Contains("s1", cluster.IdentityIds);
        Assert.Contains("s2", cluster.IdentityIds);
    }

    [Fact]
    public void ExcludedGroupMembershipIsPreservedAndCandidateCanJoin()
    {
        var exc1 = Vector(0.0f, 1.0f, 0.0f);
        var exc2 = Vector(0.05f, 0.98f, 0.0f);
        var newMember = Vector(-0.02f, 0.99f, 0.0f);

        var scenes = new List<SceneClusteringScene>
        {
            new("exc-1", exc1, null, null),
            new("exc-2", exc2, null, null),
            new("new-candidate", newMember, null, null),
        };

        var states = new SceneIdentityReviewStates
        {
            SettledIds = new HashSet<string>(),
            ExcludedGroupIds = new Dictionary<string, string>
            {
                ["exc-1"] = "group-1",
                ["exc-2"] = "group-1",
            },
            PinnedUnsortedIds = new HashSet<string>(),
            NegativeLocationIds = new Dictionary<string, IReadOnlySet<string>>()
        };

        var result = SceneClustering.Run(scenes, [], states, DefaultOptions);

        Assert.Empty(result.AnonymousClusters);
        var group = Assert.Single(result.ExcludedGroups);
        Assert.Equal("group-1", group.GroupId);
        Assert.Contains("exc-1", group.IdentityIds);
        Assert.Contains("exc-2", group.IdentityIds);
        Assert.Contains("new-candidate", group.IdentityIds);
    }

    private static float[] UnitVector(int axis, int dimensions = 512)
    {
        var vector = new float[dimensions];
        vector[axis] = 1.0f;
        return vector;
    }

    private static float[] Vector(float x, float y, float z = 0f, int dimensions = 512)
    {
        var vector = new float[dimensions];
        vector[0] = x;
        vector[1] = y;
        vector[2] = z;
        var sumSq = 0.0;
        for (var i = 0; i < vector.Length; i++) sumSq += vector[i] * vector[i];
        var norm = Math.Sqrt(sumSq);
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / norm);
        }
        return vector;
    }
}
