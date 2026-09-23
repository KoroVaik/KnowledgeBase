namespace KnowledgeBase.Worker.FaceAnalysis;

// A detection fix re-runs AnalyzeFaces under a new version and supersedes the older versions'
// occurrences; re-ranking those would write their stale boxes back into review, so everything
// that reads or re-scores detections must count only the current version's rows.
public static class FaceAnalysisPipeline
{
    public const string CurrentDetectionVersion = "face-analysis/v4";
}
