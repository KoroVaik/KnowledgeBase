using Backend.Controllers.Auth.Configuration;
using Backend.Infrastructure.Hosting;
using Backend.Infrastructure.Storage;

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

builder.Services.AddAuthFeature(builder.Configuration);
builder.Services.AddAssetStorage(builder.Configuration);

var app = builder.Build();

app.UseKnowledgeBasePipeline();

app.MapHealthChecks("/health");
app.MapControllers();

// Deep links have to reach the SPA, but a bare catch-all would answer a mistyped /api path
// with index.html and HTTP 200 — fetch() would read that markup as success.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
