using KnowledgeBase.Api.Controllers.Auth.Configuration;
using KnowledgeBase.Api.Controllers.Events.Configuration;
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

// The pipeline runs in the Worker; the API only advertises its text-size limit via /api/features.
builder.Services.Configure<PipelineOptions>(builder.Configuration.GetSection(PipelineOptions.SectionName));

var app = builder.Build();

app.MigrateDatabase();
app.UseKnowledgeBasePipeline();

app.MapHealthChecks("/health");
app.MapControllers();

// A bare catch-all would answer a mistyped /api path with index.html + 200; fetch() reads that as success.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
