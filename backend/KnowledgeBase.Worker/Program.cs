using KnowledgeBase.Core.Observability;
using Serilog;
using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Ai.Configuration;
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
using KnowledgeBase.Core.Pipeline.SceneAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using KnowledgeBase.Worker.FaceComparison;
using KnowledgeBase.Worker.FaceRecognitionComparison;

Log.Logger = LoggingSetup.Bootstrap();
try
{
    if (args.Length == 2 && args[0] == "compare-faces")
    {
        await FaceComparisonCommand.RunAsync(args[1]);
        return;
    }

    if ((args.Length is 2 or 3) && args[0] == "compare-face-pack")
    {
        await FaceComparisonPackCommand.RunAsync(args[1], args.Length == 3 ? args[2] : null);
        return;
    }

    if (args.Length == 2 && args[0] == "compare-face-report")
    {
        await FaceComparisonPackCommand.RunReportAsync(args[1]);
        return;
    }

    var builder = WebApplication.CreateBuilder(args);

    builder.UseLocalOverrides();
    builder.AddObservability("worker");

    builder.Services.AddDatabase(builder.Configuration);

    // The worker only needs IAssetContentReader; IAssetStorage / IAssetLinkSigner come with it.
    builder.Services.AddAssetStorage(builder.Configuration);

    builder.Services.AddContentAnalyzer(builder.Configuration);
    builder.Services.AddContentPipeline(builder.Configuration);
    builder.Services.Configure<FaceAnalysisOptions>(builder.Configuration.GetSection(FaceAnalysisOptions.SectionName));
    builder.Services.AddOptions<FaceComparisonOptions>()
        .Bind(builder.Configuration.GetSection(FaceComparisonOptions.SectionName))
        .Validate(options => options.ScrfdThreshold is > 0 and <= 1 && options.YuNetThreshold is > 0 and <= 1
            && options.NmsThreshold is > 0 and <= 1 && !string.IsNullOrWhiteSpace(options.ModelDirectory), "Invalid face comparison options.")
        .ValidateOnStart();
    builder.Services.AddSingleton<ComparisonModelFiles>();
    builder.Services.AddSingleton<FaceComparisonRunner>();
    builder.Services.AddScoped<IPipelineHandler, FaceComparisonHandler>();
    builder.Services.AddOptions<FaceRecognitionComparisonOptions>()
        .Bind(builder.Configuration.GetSection(FaceRecognitionComparisonOptions.SectionName))
        .Validate(options => !string.IsNullOrWhiteSpace(options.ModelDirectory), "Invalid face recognition comparison options.")
        .ValidateOnStart();
    builder.Services.AddSingleton<RecognitionComparisonModelFiles>();
    builder.Services.AddSingleton<FaceRecognitionComparisonRunner>();
    builder.Services.AddScoped<IPipelineHandler, FaceRecognitionComparisonHandler>();
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
    builder.Services.AddOptions<SceneClusteringOptions>()
        .Bind(builder.Configuration.GetSection(SceneClusteringOptions.SectionName))
        .Validate(
            options => options.HintThreshold > 0 && options.HintThreshold <= options.LocationJoinThreshold
                && options.LocationJoinThreshold <= 1 && options.ClusterThreshold is > 0 and <= 1,
            "SceneClustering needs 0 < HintThreshold <= LocationJoinThreshold <= 1 and 0 < ClusterThreshold <= 1.")
        .ValidateOnStart();
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
    builder.Services.AddSingleton<ISceneEmbedder, VprSceneEmbedder>();
    builder.Services.AddScoped<IPipelineHandler, FaceAnalysisHandler>();
    builder.Services.AddScoped<IPipelineHandler, FaceValidationHandler>();
    builder.Services.AddScoped<IPipelineHandler, AssetFingerprintHandler>();
    builder.Services.AddScoped<IPipelineHandler, FaceRescoreHandler>();
    builder.Services.AddScoped<IPipelineHandler, ClusterFacesHandler>();
    builder.Services.AddScoped<IPipelineHandler, FaceModelMigrationHandler>();
    builder.Services.AddScoped<IPipelineHandler, SceneAnalysisHandler>();
    builder.Services.AddScoped<IPipelineHandler, ClusterScenesHandler>();
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
    var app = builder.Build();

    app.MapPost("/api/worker/wake", (HttpContext context, IJobWakeSignal wakeSignal, IOptions<EventsBridgeOptions> options, ILoggerFactory loggerFactory) =>
    {
        if (!context.Request.Headers.TryGetValue("X-Ingest-Token", out var token) || token != options.Value.IngestToken)
        {
            return Results.Unauthorized();
        }

        var logger = loggerFactory.CreateLogger("WorkerWebhook");
        logger.LogInformation("Received wake webhook from API");
        wakeSignal.Trigger();
        return Results.Accepted();
    });

    app.MapGet("/health", () => Results.Ok("Worker online"));

    await app.Services.GetRequiredService<ArcFaceModelDownloader>().EnsureAsync(CancellationToken.None);

    // One-off analyzer smoke test, then exit before the poll loop.
    if (AnalyzeCommand.Matches(args))
    {
        await AnalyzeCommand.RunAsync(app.Services, args);
        return;
    }

    await app.WaitForMigrationsAsync(TimeSpan.FromSeconds(30));

    // Face models may have changed since this archive was last analysed (an embedder swap, a
    // detection fix): queue the self-healing migration before the poll loop takes normal jobs.
    try
    {
        using var scope = app.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<KnowledgeBaseDbContext>();
        await FaceModelMigrationQueue.EnqueueIfGapAsync(
            database,
            scope.ServiceProvider.GetRequiredService<IFaceAnalyzer>(),
            CancellationToken.None);
        await FaceValidationBackfill.EnqueueAsync(database, scope.ServiceProvider.GetRequiredService<IOptions<OllamaOptions>>().Value.Model, CancellationToken.None);
        // An archive upgraded to clustering has faces/scenes but no grouping yet, and nothing else would
        // trigger the first one until the next upload or review.
        if (!await database.FaceClusteringRuns.AnyAsync() && await database.FaceOccurrences.AnyAsync()
            && await ClusterFacesQueue.EnqueueAsync(database, CancellationToken.None))
            await database.SaveChangesAsync();
        if (!await database.SceneClusteringRuns.AnyAsync() && await database.SceneIdentities.AnyAsync()
            && await ClusterScenesQueue.EnqueueAsync(database, CancellationToken.None))
            await database.SaveChangesAsync();
    }
    catch (Exception error)
    {
        // The database may still be unreachable; the next start re-checks. Never blocks the worker.
        Log.Warning(error, "Face migration startup check skipped");
    }

    await app.RunAsync();


}
catch (Exception error)
{
    Log.Fatal(error, "Host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
