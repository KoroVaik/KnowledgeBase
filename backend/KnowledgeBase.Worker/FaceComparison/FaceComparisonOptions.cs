namespace KnowledgeBase.Worker.FaceComparison;

public sealed class FaceComparisonOptions
{
    public const string SectionName = "FaceComparison";
    public string ModelDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnowledgeBase", "models", "face-comparison");
    public float ScrfdThreshold { get; set; } = 0.5f;
    public float YuNetThreshold { get; set; } = 0.9f;
    public float NmsThreshold { get; set; } = 0.4f;
}
