namespace KnowledgeBase.Worker.SceneObservations;

public sealed class SceneObservationOptions
{
    public const string SectionName = "SceneObservations";

    public int MaximumObservations { get; set; } = 10;
}
