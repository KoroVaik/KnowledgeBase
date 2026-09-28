namespace KnowledgeBase.Core.Pipeline;

public interface IJobWakeSignal
{
    void Trigger();
    Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class JobWakeSignal : IJobWakeSignal
{
    // Initialized to 1 so the worker always checks the queue once on startup.
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public void Trigger()
    {
        if (_semaphore.CurrentCount == 0)
        {
            try
            {
                _semaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // Already signaled.
            }
        }
    }

    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        return await _semaphore.WaitAsync(timeout, cancellationToken);
    }
}
