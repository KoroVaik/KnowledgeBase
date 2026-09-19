namespace KnowledgeBase.Core.FaceAnalysis;

/// <summary>Thresholds for the ClusterFaces job. The defaults are unmeasured placeholders from
/// typical ArcFace behaviour, like FaceAnalysis:Center/Scale — calibrate them on labelled data
/// before reading anything into a score near one.</summary>
public sealed class FaceClusteringOptions
{
    public const string SectionName = "FaceClustering";

    /// <summary>Below this a face never joins a person or an ignored group, however close the next best is.</summary>
    public double PersonJoinThreshold { get; set; } = 0.45;

    /// <summary>Agglomerative clustering merges anonymous faces while their average-linkage cosine reaches this.</summary>
    public double ClusterThreshold { get; set; } = 0.45;

    /// <summary>Below this an anonymous cluster gets no "looks like" hint; above PersonJoinThreshold it would not be anonymous.</summary>
    public double HintThreshold { get; set; } = 0.30;
}
