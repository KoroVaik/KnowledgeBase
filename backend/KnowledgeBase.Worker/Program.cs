using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Hosting;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.Pipeline.Extraction;
using KnowledgeBase.Core.Pipeline.FaceAnalysis;
using KnowledgeBase.Core.RealTime;
using KnowledgeBase.Core.SceneAnalysis;
using KnowledgeBase.Core.Storage;
using KnowledgeBase.Worker;
using KnowledgeBase.Worker.FaceAnalysis;
using KnowledgeBase.Worker.Extraction;
using KnowledgeBase.Worker.EventClustering;
using KnowledgeBase.Worker.SceneAnalysis;
using KnowledgeBase.Worker.SceneObservations;
using ElBruno.LocalEmbeddings.ImageEmbeddings.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.UseLocalOverrides();

builder.Services.AddDatabase(builder.Configuration);

// The worker only needs IAssetContentReader; IAssetStorage / IAssetLinkSigner come with it.
builder.Services.AddAssetStorage(builder.Configuration);

builder.Services.AddContentAnalyzer(builder.Configuration);
builder.Services.AddContentPipeline(builder.Configuration);
builder.Services.Configure<FaceAnalysisOptions>(builder.Configuration.GetSection(FaceAnalysisOptions.SectionName));
builder.Services.Configure<VisualAnalysisOptions>(builder.Configuration.GetSection(VisualAnalysisOptions.SectionName));
builder.Services.Configure<SceneObservationOptions>(builder.Configuration.GetSection(SceneObservationOptions.SectionName));
builder.Services.Configure<EventClusteringOptions>(builder.Configuration.GetSection(EventClusteringOptions.SectionName));
builder.Services.AddOptions<FaceClusteringOptions>()
    .Bind(builder.Configuration.GetSection(FaceClusteringOptions.SectionName))
    .Validate(
        options => options.HintThreshold > 0 && options.HintThreshold <= options.PersonJoinThreshold
            && options.PersonJoinThreshold <= 1 && options.ClusterThreshold is > 0 and <= 1,
        "FaceClustering needs 0 < HintThreshold <= PersonJoinThreshold <= 1 and 0 < ClusterThreshold <= 1.")
    .ValidateOnStart();
var visualAnalysisOptions = builder.Configuration.GetSection(VisualAnalysisOptions.SectionName).Get<VisualAnalysisOptions>() ?? new VisualAnalysisOptions();
var visualModelDirectory = visualAnalysisOptions.ModelDirectory
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnowledgeBase", "models", "clip");
builder.Services.AddImageEmbeddings(options =>
{
    options.ModelDirectory = visualModelDirectory;
    options.EnsureModelDownloaded = visualAnalysisOptions.EnsureModelDownloaded;
});
builder.Services.AddSingleton(sp =>
{
    var faceOptions = sp.GetRequiredService<IOptions<FaceAnalysisOptions>>().Value;
    var configuredPath = faceOptions.RecognitionModelPath;
    if (Path.IsPathRooted(configuredPath) && File.Exists(configuredPath)) return new ArcFaceEmbedder(configuredPath);
    var candidates = new[]
    {
        Path.GetFullPath(configuredPath),
        Path.Combine(AppContext.BaseDirectory, configuredPath),
        Path.Combine(Directory.GetCurrentDirectory(), configuredPath),
        // The documented way to start the worker is `dotnet run` from the repo root, where
        // the models folder hangs off the project directory rather than the working one.
        Path.Combine(Directory.GetCurrentDirectory(), "backend", "KnowledgeBase.Worker", configuredPath),
    };
    var found = candidates.FirstOrDefault(File.Exists);
    return new ArcFaceEmbedder(found ?? throw new FileNotFoundException(
        $"The ArcFace embedding model was not found. Looked at: {string.Join("; ", candidates.Distinct())}.", configuredPath));
});
builder.Services.AddSingleton<ArcFaceModelDownloader>();
builder.Services.AddSingleton<IFaceAnalyzer, FaceOnnxFaceAnalyzer>();
builder.Services.AddSingleton<ISceneEmbedder, ClipSceneEmbedder>();
builder.Services.AddScoped<IPipelineHandler, FaceAnalysisHandler>();
builder.Services.AddScoped<IPipelineHandler, AssetFingerprintHandler>();
builder.Services.AddScoped<IPipelineHandler, FaceRescoreHandler>();
builder.Services.AddScoped<IPipelineHandler, ClusterFacesHandler>();
builder.Services.AddScoped<IPipelineHandler, FaceModelMigrationHandler>();
builder.Services.AddScoped<IPipelineHandler, SceneAnalysisHandler>();
builder.Services.AddScoped<IPipelineHandler, SceneObservationHandler>();
builder.Services.AddScoped<IPipelineHandler, EventCandidateHandler>();

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

await host.Services.GetRequiredService<ArcFaceModelDownloader>().EnsureAsync(CancellationToken.None);

// One-off analyzer smoke test, then exit before the poll loop.
if (AnalyzeCommand.Matches(args))
{
    await AnalyzeCommand.RunAsync(host.Services, args);
    return;
}

await host.WaitForMigrationsAsync(TimeSpan.FromSeconds(30));

// Face models may have changed since this archive was last analysed (an embedder swap, a
// detection fix): queue the self-healing migration before the poll loop takes normal jobs.
try
{
    using var scope = host.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<KnowledgeBaseDbContext>();
    await FaceModelMigrationQueue.EnqueueIfGapAsync(
        database,
        scope.ServiceProvider.GetRequiredService<IFaceAnalyzer>(),
        CancellationToken.None);
    // An archive upgraded to clustering has faces but no grouping yet, and nothing else would
    // trigger the first one until the next upload or review.
    if (!await database.FaceClusteringRuns.AnyAsync() && await database.FaceOccurrences.AnyAsync()
        && await ClusterFacesQueue.EnqueueAsync(database, CancellationToken.None))
        await database.SaveChangesAsync();
}
catch (Exception error)
{
    // The database may still be unreachable; the next start re-checks. Never blocks the worker.
    Console.Error.WriteLine($"[face-migration] Start-up check skipped: {error.Message}");
}

host.Run();
