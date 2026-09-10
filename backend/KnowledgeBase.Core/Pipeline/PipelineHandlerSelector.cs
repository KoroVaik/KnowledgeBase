using KnowledgeBase.Core.Persistence;

namespace KnowledgeBase.Core.Pipeline;

// Indexes the registered handlers by kind once. Two handlers claiming the same kind is a
// wiring mistake and fails here, at construction, not on the job that happens to hit it.
public sealed class PipelineHandlerSelector
{
    private readonly IReadOnlyDictionary<JobKind, IPipelineHandler> _byKind;

    public PipelineHandlerSelector(IEnumerable<IPipelineHandler> handlers)
    {
        _byKind = handlers.ToDictionary(handler => handler.Kind);
    }

    public IPipelineHandler For(JobKind kind) =>
        _byKind.TryGetValue(kind, out var handler)
            ? handler
            : throw new InvalidOperationException($"No handler is registered for {kind} jobs.");
}
