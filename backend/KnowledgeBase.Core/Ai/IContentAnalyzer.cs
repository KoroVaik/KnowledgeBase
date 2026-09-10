namespace KnowledgeBase.Core.Ai;

public sealed record AnalysisImage(byte[] Bytes, string ContentType);

public interface IContentAnalyzer
{
    // Called before the first analysis (by the worker, and by the analyze CLI command): a
    // pulled-model check that fails with "ollama pull ..." rather than a cryptic 404 mid-job.
    Task EnsureModelAvailableAsync(CancellationToken cancellationToken);

    // Runs one structured-output call and deserialises the reply into T. Throws
    // ContentTooLargeException when the model truncated the answer against its context window;
    // the caller validates that T is actually usable.
    Task<T> RunAsync<T>(AiTask task, CancellationToken cancellationToken)
        where T : class;
}
