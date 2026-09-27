using System.Text;
using KnowledgeBase.Api.Controllers.Events;
using KnowledgeBase.Api.Controllers.Events.Configuration;
using KnowledgeBase.Core.RealTime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeBase.Tests;

public sealed class EventsControllerTests
{
    [Fact]
    public async Task QuietStreamFlushesACommentBeforeWaitingForChanges()
    {
        using var body = new FlushCaptureStream();
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        var controller = new EventsController(
            new ChangeNotifier(NullLogger<ChangeNotifier>.Instance),
            Options.Create(new EventsOptions()),
            NullLogger<EventsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        var streaming = controller.Stream(cancellation.Token);
        try
        {
            var firstFrame = await body.FirstFlush.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.StartsWith(":", firstFrame);
            Assert.EndsWith("\n\n", firstFrame);
            Assert.DoesNotContain("data:", firstFrame);
            Assert.Equal("text/event-stream", context.Response.ContentType);
            Assert.False(streaming.IsCompleted);
        }
        finally
        {
            await cancellation.CancelAsync();
            await streaming.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private sealed class FlushCaptureStream : MemoryStream
    {
        public TaskCompletionSource<string> FirstFlush { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (Length > 0)
            {
                FirstFlush.TrySetResult(Encoding.UTF8.GetString(ToArray()));
            }
            return base.FlushAsync(cancellationToken);
        }
    }
}
