namespace KnowledgeBase.Worker.SceneAnalysis;

public sealed class VisualAnalysisOptions
{
    public const string SectionName = "VisualAnalysis";

    public string? ModelDirectory { get; set; }

    public bool EnsureModelDownloaded { get; set; } = true;

    public int CandidateCount { get; set; } = 5;
}
