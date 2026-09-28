using KnowledgeBase.Api.Controllers.Events.Configuration;
using KnowledgeBase.Api.Infrastructure.RealTime;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.RealTime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeBase.Tests;

public sealed class WorkerWebSocketNotificationTests
{
    [Fact]
    public async Task JobWakeSignal_InitialRunAndTrigger_WorkAsExpected()
    {
        var signal = new JobWakeSignal();

        // 1. Initially signaled on creation
        var initialWait = await signal.WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);
        Assert.True(initialWait);

        // 2. Second wait should timeout when no trigger occurred
        var timeoutWait = await signal.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);
        Assert.False(timeoutWait);

        // 3. Triggering wakes it up immediately
        signal.Trigger();
        var triggeredWait = await signal.WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);
        Assert.True(triggeredWait);
    }

    [Fact]
    public void WorkerWebhookNotifier_CanBeConstructedAndDisposedCleanly()
    {
        var changeNotifier = new ChangeNotifier(NullLogger<ChangeNotifier>.Instance);
        var options = Options.Create(new EventsOptions { WorkerWakeUrl = "" });
        var notifier = new WorkerWebhookNotifier(new DummyHttpClientFactory(), options, changeNotifier, NullLogger<WorkerWebhookNotifier>.Instance);

        // Should not throw when notifying
        notifier.NotifyJobQueued();

        // Publishing a relevant event should not throw
        changeNotifier.Publish(new ChangeEvent(ChangeResources.Assets, ChangeActions.Created, "test.txt"));

        notifier.Dispose();
    }

    private sealed class DummyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }
}
