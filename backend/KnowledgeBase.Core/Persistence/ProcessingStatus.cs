namespace KnowledgeBase.Core.Persistence;

public enum ProcessingStatus
{
    Pending,
    Running,
    Done,
    Failed,

    // Terminal, like Failed, but the pipeline chose not to run rather than tried and broke:
    // the source does not fit the model's context. Retrying the same file cannot help, so the
    // worker never re-queues it - the user has to split it or upload a shorter piece.
    Skipped,
}
