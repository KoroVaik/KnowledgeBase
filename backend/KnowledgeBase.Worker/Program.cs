using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Hosting;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.Pipeline.Extraction;
using KnowledgeBase.Core.RealTime;
using KnowledgeBase.Core.Storage;
using KnowledgeBase.Worker;
using KnowledgeBase.Worker.Extraction;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.UseLocalOverrides();

builder.Services.AddDatabase(builder.Configuration);

// The worker only needs IAssetContentReader; IAssetStorage / IAssetLinkSigner come with it.
builder.Services.AddAssetStorage(builder.Configuration);

builder.Services.AddContentAnalyzer(builder.Configuration);
builder.Services.AddContentPipeline(builder.Configuration);

// PDF extractor + PdfPig live here, not Core, so the API image stays free of them.
builder.Services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
builder.Services.AddSingleton<ISourceExtractor, PdfSourceExtractor>();

// Posts PipelineWorker's change hints to the API. No-op without ApiBaseUrl / IngestToken.
builder.Services.Configure<EventsBridgeOptions>(builder.Configuration.GetSection(EventsBridgeOptions.SectionName));
builder.Services.AddHttpClient(HttpChangeNotifier.ClientName, (serviceProvider, client) =>
{
    var bridge = serviceProvider.GetRequiredService<IOptions<EventsBridgeOptions>>().Value;

    if (!string.IsNullOrWhiteSpace(bridge.ApiBaseUrl))
    {
        client.BaseAddress = new Uri(bridge.ApiBaseUrl);
    }

    // Short: a slow / cold API must not hold up the poll loop. The note is saved either way.
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<IChangeNotifier, HttpChangeNotifier>();

var host = builder.Build();

// One-off analyzer smoke test, then exit before the poll loop.
if (AnalyzeCommand.Matches(args))
{
    await AnalyzeCommand.RunAsync(host.Services, args);
    return;
}

host.Run();
