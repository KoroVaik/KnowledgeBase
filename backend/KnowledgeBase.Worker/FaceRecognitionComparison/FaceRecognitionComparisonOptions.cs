namespace KnowledgeBase.Worker.FaceRecognitionComparison;

public sealed class FaceRecognitionComparisonOptions
{
    public const string SectionName = "FaceRecognitionComparison";

    public string ModelDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnowledgeBase", "models", "face-recognition-comparison");
}
