using KnowledgeBase.Core.Persistence;

namespace KnowledgeBase.Core.Pipeline;

// One per JobKind. Builds the note a job asks for and adds it (plus its links and tags) to
// the DbContext - the worker owns the surrounding SaveChanges, retry, Skipped/Failed and SSE.
// A source it cannot use throws SkippableContentException; anything else is a retriable fault.
public interface IPipelineHandler
{
    JobKind Kind { get; }

    Task<Note> HandleAsync(ProcessingJob job, CancellationToken cancellationToken);
}
