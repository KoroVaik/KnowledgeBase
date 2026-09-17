namespace KnowledgeBase.Core.SceneAnalysis;

public interface ISceneEmbedder
{
    string ModelKey { get; }

    string ConfigurationHash { get; }

    Task<float[]> EmbedAsync(byte[] imageBytes, CancellationToken cancellationToken);
}
