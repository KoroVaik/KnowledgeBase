namespace KnowledgeBase.Core.Ai;

// The source does not fit the model's context window. Thrown before the call (the worker
// measured the text) or after it (Ollama reported it truncated the response). Either way
// retrying the same input cannot help, so the pipeline marks the job Skipped rather than
// Failed and does not re-queue it.
public sealed class ContentTooLargeException(string message) : InvalidOperationException(message);
