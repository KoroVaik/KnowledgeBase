using KnowledgeBase.Api.Controllers.Auth.Configuration;
using KnowledgeBase.Api.Controllers.Events.Configuration;
using KnowledgeBase.Api.Infrastructure.Features;
using KnowledgeBase.Api.Infrastructure.Hosting;
using KnowledgeBase.Core.Hosting;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.RealTime;
using KnowledgeBase.Core.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.UseAssignedPort();
builder.UseLocalOverrides();

builder.Services.AddControllers();

// The [controller] token takes the class name as written, so the OpenAPI schema would carry
// /api/Auth while every caller uses /api/auth. Matching is case-insensitive, the generated
// client would not be.
builder.Services.AddRouting(options => options.LowercaseUrls = true);
builder.Services.AddOpenApiDocumentation();
builder.Services.AddHealthChecks();
builder.Services.AddProxyAwareHosting();

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddFeatureFlags(builder.Configuration);
builder.Services.AddAuthFeature(builder.Configuration);
builder.Services.AddAssetStorage(builder.Configuration);
builder.Services.AddRealTimeUpdates();
builder.Services.Configure<EventsOptions>(builder.Configuration.GetSection(EventsOptions.SectionName));

// The pipeline itself runs in the Worker process, but the API advertises its text-size limit
// through /api/features so the UI can warn before a too-large upload. Options only - no worker.
builder.Services.Configure<PipelineOptions>(builder.Configuration.GetSection(PipelineOptions.SectionName));

var app = builder.Build();

app.MigrateDatabase();
app.UseKnowledgeBasePipeline();

app.MapHealthChecks("/health");
app.MapControllers();

// Deep links have to reach the SPA, but a bare catch-all would answer a mistyped /api path
// with index.html and HTTP 200 — fetch() would read that markup as success.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
