using System.Text.RegularExpressions;

const string DevFrontendCorsPolicy = "DevFrontend";

// Soft cap only: ASP.NET has already buffered the multipart body by the time this is
// checked (framework default 128 MB). Needs a streaming path once real uploads land.
const long MaxUploadBytes = 25L * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Regex rather than a fixed WithOrigins list: the PC's LAN IP is not known up front, so
// a phone on the same Wi-Fi has to be allowed without opening the API to the internet.
var devFrontendOriginPattern = new Regex(
    @"^https?://(localhost|127\.0\.0\.1|192\.168\.\d{1,3}\.\d{1,3}|10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}):5173$");

builder.Services.AddCors(options =>
{
    options.AddPolicy(DevFrontendCorsPolicy, policy => policy
        .SetIsOriginAllowed(origin => devFrontendOriginPattern.IsMatch(origin))
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // Skipped in Development on purpose: the SPA talks to http://localhost:5244, and a
    // 307 to the HTTPS endpoint would fail the CORS preflight / dev-certificate check.
    app.UseHttpsRedirection();
}

app.UseCors(DevFrontendCorsPolicy);

// Created eagerly so concurrent first uploads cannot race on it.
var assetsDirectory = Path.Combine(app.Environment.ContentRootPath, "data", "assets");
Directory.CreateDirectory(assetsDirectory);

// Stub: no AI processing, no .md generation, no indexing yet.
app.MapPost("/api/notes/upload", async (IFormFile file, CancellationToken cancellationToken) =>
{
    if (file.Length == 0)
    {
        return Results.BadRequest(new { error = "File is empty." });
    }

    if (file.Length > MaxUploadBytes)
    {
        return Results.BadRequest(new { error = $"File exceeds the {MaxUploadBytes / (1024 * 1024)} MB limit." });
    }

    // Never trust the client-supplied name: keep only a plausible extension.
    var originalFileName = Path.GetFileName(file.FileName);
    var extension = Path.GetExtension(originalFileName);
    if (extension.Length > 16 || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
    {
        extension = string.Empty;
    }

    var id = Guid.NewGuid().ToString("N");
    var storedFileName = id + extension;

    await using (var target = File.Create(Path.Combine(assetsDirectory, storedFileName)))
    {
        await file.CopyToAsync(target, cancellationToken);
    }

    return Results.Ok(new UploadedAssetResponse(
        id,
        storedFileName,
        originalFileName,
        file.ContentType,
        file.Length,
        DateTimeOffset.UtcNow));
})
.WithName("UploadNoteAsset")
.WithOpenApi()
// Minimal-API form binding opts into antiforgery validation; there is no token flow
// here yet, so it is disabled explicitly rather than failing at runtime.
.DisableAntiforgery();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

record UploadedAssetResponse(
    string Id,
    string StoredFileName,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAtUtc);
