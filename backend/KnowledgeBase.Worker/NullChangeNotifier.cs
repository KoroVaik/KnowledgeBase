using KnowledgeBase.Core.RealTime;

namespace KnowledgeBase.Worker;

// The worker runs on a different machine from the API, so it cannot reach the API's in-memory
// notifier - there is no worker->API SSE bridge yet. PipelineWorker still publishes a
// "notes/created" hint after each note; here it goes nowhere, and the front end picks the note
// up on its next GET /api/notes. When a bridge arrives this is the one registration that changes.
public sealed class NullChangeNotifier : IChangeNotifier
{
    public void Publish(ChangeEvent change)
    {
    }

    public ChangeSubscription Subscribe() =>
        throw new NotSupportedException("The worker does not serve event streams.");
}
