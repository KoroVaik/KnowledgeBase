using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Hosting;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.RealTime;
using KnowledgeBase.Core.Storage;
using KnowledgeBase.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.UseLocalOverrides();

builder.Services.AddDatabase(builder.Configuration);

// Registers IAssetContentReader (the worker's real need) plus IAssetStorage / IAssetLinkSigner,
// which it never resolves - cheap singletons, and one registration path shared with the API.
builder.Services.AddAssetStorage(builder.Configuration);

builder.Services.AddContentAnalyzer(builder.Configuration);
builder.Services.AddContentPipeline(builder.Configuration);

// No SSE bridge to the API yet - see NullChangeNotifier.
builder.Services.AddSingleton<IChangeNotifier, NullChangeNotifier>();

var host = builder.Build();

// Borrows the built container for a one-off analyzer smoke test, then exits before the
// polling loop starts.
if (AnalyzeCommand.Matches(args))
{
    await AnalyzeCommand.RunAsync(host.Services, args);
    return;
}

host.Run();
