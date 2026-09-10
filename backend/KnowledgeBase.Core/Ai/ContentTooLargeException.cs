namespace KnowledgeBase.Core.Ai;

// The source does not fit the model's context window. Thrown before the call (the worker
// measured the text) or after it (Ollama reported it truncated the response). A kind of
// SkippableContentException: retrying the same input cannot help.
public sealed class ContentTooLargeException(string message) : SkippableContentException(message);
