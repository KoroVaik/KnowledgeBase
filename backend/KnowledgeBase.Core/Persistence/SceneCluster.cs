namespace KnowledgeBase.Core.Persistence;

// One clustering execution over all open scenes of the archive. Global (no asset), unlike
// PhotoAnalysisRun which stays per-asset: the review screen takes the clusters of the
// latest run from here in one query.
public sealed class SceneClusteringRun
{
    public required string Id { get; init; }
    public required string PipelineVersion { get; init; }
    public required string ConfigurationHash { get; init; }
    public required DateTime CompletedAtUtc { get; init; }
}

public enum SceneClusterKind { Location, Anonymous, Unsorted, Excluded }

// A temporary algorithm output for one clustering run: the location row's joined scenes, one
// anonymous group, all Unsorted scenes of the run, or the current scenes of an excluded group.
public sealed class SceneCluster
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required SceneClusterKind Kind { get; init; }
    // Evidence pointers without an FK (same rationale as PhotoAnalysisCandidate.ProposedTargetId):
    // a location deleted later must not take the historical clustering output with it.
    public string? LocationId { get; init; }
    public string? HintLocationId { get; init; }
    public double? HintScore { get; init; }
    public string? ExcludedGroupId { get; set; }
    public required DateTime CreatedAtUtc { get; init; }
}

// A stable user record: scenes the user excluded. It survives re-clustering on purpose; its
// current membership is derived (latest Ignored decision per identity, plus the scenes the
// latest clustering run joined into it), never stored here.
public sealed class ExcludedSceneGroup
{
    public required string Id { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}
