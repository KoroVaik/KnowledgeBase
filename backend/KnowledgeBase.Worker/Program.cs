using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Hosting;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.RealTime;
using KnowledgeBase.Core.Storage;
using KnowledgeBase.Worker;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.UseLocalOverrides();

builder.Services.AddDatabase(builder.Configuration);

// Registers IAssetContentReader (the worker's real need) plus IAssetStorage / IAssetLinkSigner,
// which it never resolves - cheap singletons, and one registration path shared with the API.
builder.Services.AddAssetStorage(builder.Configuration);

builder.Services.AddContentAnalyzer(builder.Configuration);
builder.Services.AddContentPipeline(builder.Configuration);

// Posts PipelineWorker's change hints to the API so open browsers refresh live. With no
// ApiBaseUrl / IngestToken configured it is a no-op - see HttpChangeNotifier.
builder.Services.Configure<EventsBridgeOptions>(builder.Configuration.GetSection(EventsBridgeOptions.SectionName));
builder.Services.AddHttpClient(HttpChangeNotifier.ClientName, (serviceProvider, client) =>
{
    var bridge = serviceProvider.GetRequiredService<IOptions<EventsBridgeOptions>>().Value;

    if (!string.IsNullOrWhiteSpace(bridge.ApiBaseUrl))
    {
        client.BaseAddress = new Uri(bridge.ApiBaseUrl);
    }

    // Short on purpose: a slow or cold-starting API must not hold up the poll loop. The note
    // is saved regardless of whether this ping lands.
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<IChangeNotifier, HttpChangeNotifier>();

var host = builder.Build();

// Borrows the built container for a one-off analyzer smoke test, then exits before the
// polling loop starts.
if (AnalyzeCommand.Matches(args))
{
    await AnalyzeCommand.RunAsync(host.Services, args);
    return;
}

host.Run();
