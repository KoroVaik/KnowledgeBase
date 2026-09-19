namespace KnowledgeBase.Core.Persistence;

// One clustering execution over all open faces of the archive. Global (no asset), unlike
// PhotoAnalysisRun which stays per-asset because its AssetId is required: the review screen
// takes the clusters of the latest run from here in one query.
public sealed class FaceClusteringRun
{
    public required string Id { get; init; }
    public required string PipelineVersion { get; init; }
    public required string ConfigurationHash { get; init; }
    public required DateTime CompletedAtUtc { get; init; }
}

public enum FaceClusterKind { Person, Anonymous, Unsorted, Ignored }

// A temporary algorithm output for one clustering run: the person row's joined faces, one
// anonymous group, all Unsorted faces of the run, or the current faces of an ignored group.
public sealed class FaceCluster
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required FaceClusterKind Kind { get; init; }
    // Evidence pointers without an FK (same rationale as PhotoAnalysisCandidate.ProposedTargetId):
    // a person deleted later must not take the historical clustering output with it.
    public string? PersonId { get; init; }
    public string? HintPersonId { get; init; }
    public double? HintScore { get; init; }
    public string? IgnoredGroupId { get; set; }
    public required DateTime CreatedAtUtc { get; init; }
}

// A stable user record: faces the user ignored. It survives re-clustering on purpose; its
// current membership is derived (latest Ignored decision per identity, plus the faces the
// latest clustering run joined into it), never stored here.
public sealed class IgnoredFaceGroup
{
    public required string Id { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}
