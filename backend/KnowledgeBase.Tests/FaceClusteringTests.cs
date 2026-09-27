using KnowledgeBase.Core.FaceAnalysis;
using Xunit;

namespace KnowledgeBase.Tests;

public sealed class FaceClusteringTests
{
    private static readonly FaceClusteringOptions DefaultOptions = new();
    private static readonly FaceIdentityReviewStates EmptyStates = new(
        new HashSet<string>(),
        new Dictionary<string, string>(),
        new HashSet<string>(),
        new Dictionary<string, IReadOnlySet<string>>());

    [Fact]
    public void CandidateJoinsPersonWhenMatchingSingleReference()
    {
        var refEmb = UnitVector(0);
        var matchingFace = UnitVector(0);
        var otherFace = UnitVector(1);

        var references = new List<PersonReferenceEmbedding>
        {
            new("p1", "Person 1", "ref-1", refEmb),
        };
        var faces = new List<FaceClusteringFace>
        {
            new("face-match", matchingFace),
            new("face-other", otherFace),
        };

        var result = FaceClustering.Run(faces, references, EmptyStates, DefaultOptions);

        var join = Assert.Single(result.PersonJoins);
        Assert.Equal("face-match", join.IdentityId);
        Assert.Equal("p1", join.PersonId);
        Assert.Equal("ref-1", join.BestReferenceFaceOccurrenceId);
        Assert.True(join.Score >= 0.99);

        Assert.Contains("face-other", result.UnsortedIdentityIds);
    }

    [Fact]
    public void OutlierReferenceDoesNotPullCrowdFacesIntoPerson()
    {
        // 3 consistent references pointing along axis 0
        var ref1 = Vector(1.0f, 0.1f, 0.0f);
        var ref2 = Vector(0.95f, 0.15f, 0.0f);
        var ref3 = Vector(0.98f, -0.05f, 0.0f);
        // 1 outlier reference pointing along axis 2 (e.g. accidental wrong photo)
        var refOutlier = Vector(0.0f, 0.0f, 1.0f);

        var references = new List<PersonReferenceEmbedding>
        {
            new("p1", "Sashinka", "ref-1", ref1),
            new("p1", "Sashinka", "ref-2", ref2),
            new("p1", "Sashinka", "ref-3", ref3),
            new("p1", "Sashinka", "ref-bad", refOutlier),
        };

        // Real person candidate (matches axis 0)
        var realCandidate = Vector(0.92f, 0.1f, 0.0f);
        // Crowd / wrong candidate (matches the outlier on axis 2, but NOT axis 0)
        var crowdCandidate = Vector(0.05f, 0.0f, 0.95f);

        var faces = new List<FaceClusteringFace>
        {
            new("face-real", realCandidate),
            new("face-crowd", crowdCandidate),
        };

        var result = FaceClustering.Run(faces, references, EmptyStates, DefaultOptions);

        // Only the real candidate joins Sashinka!
        var join = Assert.Single(result.PersonJoins);
        Assert.Equal("face-real", join.IdentityId);
        Assert.Equal("p1", join.PersonId);

        // The crowd face does NOT join Sashinka; it goes to unsorted or cluster
        Assert.DoesNotContain(result.PersonJoins, j => j.IdentityId == "face-crowd");
        Assert.Contains("face-crowd", result.UnsortedIdentityIds);
    }

    [Fact]
    public void UnmatchedFacesOfSamePersonClusterTogether()
    {
        var refEmb = UnitVector(0);
        var references = new List<PersonReferenceEmbedding>
        {
            new("p1", "Person 1", "ref-1", refEmb),
        };

        // Two faces of an unknown person (both point along axis 1)
        var unknownFace1 = Vector(0.0f, 0.98f, 0.05f);
        var unknownFace2 = Vector(0.0f, 0.95f, -0.1f);

        var faces = new List<FaceClusteringFace>
        {
            new("unknown-1", unknownFace1),
            new("unknown-2", unknownFace2),
        };

        var result = FaceClustering.Run(faces, references, EmptyStates, DefaultOptions);

        Assert.Empty(result.PersonJoins);
        var cluster = Assert.Single(result.AnonymousClusters);
        Assert.Contains("unknown-1", cluster.IdentityIds);
        Assert.Contains("unknown-2", cluster.IdentityIds);
    }

    private static float[] UnitVector(int axis, int dimensions = 512)
    {
        var vector = new float[dimensions];
        vector[axis] = 1.0f;
        return vector;
    }

    private static float[] Vector(float x, float y, float z, int dimensions = 512)
    {
        var vector = new float[dimensions];
        vector[0] = x;
        vector[1] = y;
        vector[2] = z;
        return vector;
    }
}
