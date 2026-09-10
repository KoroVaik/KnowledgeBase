namespace KnowledgeBase.Core.Ai;

// The source cannot be analysed and retrying will not change that: a PDF with no text layer,
// an empty file, a document too large for the model's context. The pipeline marks the job
// Skipped (terminal, no retry) rather than Failed. The message is shown to the user, so it
// should say what to do about it.
public class SkippableContentException(string message) : InvalidOperationException(message);
