using KnowledgeBase.Core.Observability;
using Serilog;
using KnowledgeBase.Api.Controllers.Auth.Configuration;
using KnowledgeBase.Api.Controllers.Events.Configuration;
using KnowledgeBase.Api.Controllers.Notes.Configuration;
using KnowledgeBase.Api.Infrastructure.Hosting;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Hosting;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.RealTime;
using KnowledgeBase.Core.Storage;
using Microsoft.Extensions.Options;

Log.Logger = LoggingSetup.Bootstrap();
try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.UseAssignedPort();
    builder.UseLocalOverrides();
    builder.AddObservability("api");

    builder.Services.AddControllers();
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = 429;
        options.AddPolicy("diagnostics", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst("kb:user-id")?.Value ?? "anonymous", _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    });

    // [controller] uses the class name as written; without this the schema carries /api/Auth.
    builder.Services.AddRouting(options => options.LowercaseUrls = true);
    builder.Services.AddOpenApiDocumentation();
    builder.Services.AddHealthChecks();
    builder.Services.AddProxyAwareHosting();

    builder.Services.AddDatabase(builder.Configuration);
    builder.Services.AddAuthFeature(builder.Configuration);
    builder.Services.AddAssetStorage(builder.Configuration);
    builder.Services.AddRealTimeUpdates();
    builder.Services.Configure<EventsOptions>(builder.Configuration.GetSection(EventsOptions.SectionName));
    builder.Services.Configure<ImageSourceNotesOptions>(builder.Configuration.GetSection(ImageSourceNotesOptions.SectionName));

    // The pipeline runs in the Worker; the API only advertises its text-size limit via /api/features.
    builder.Services.Configure<PipelineOptions>(builder.Configuration.GetSection(PipelineOptions.SectionName));

    // Turns the worker's raw cosine face scores into the percents the review screen shows.
    builder.Services.Configure<FaceConfidenceCalibration>(builder.Configuration.GetSection(FaceConfidenceCalibration.SectionName));

    var app = builder.Build();

    app.MigrateDatabase();
    await app.SkipPendingJobsAsync();
    app.UseKnowledgeBasePipeline();

    app.MapHealthChecks("/health");
    app.MapControllers();

    // A bare catch-all would answer a mistyped /api path with index.html + 200; fetch() reads that as success.
    app.MapFallback("/api/{**path}", () => Results.NotFound());
    app.MapFallbackToFile("index.html");

    app.Run();

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
