using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Threading.RateLimiting;

// Soft cap only: ASP.NET has already buffered the multipart body by the time this is
// checked (framework default 128 MB). Needs a streaming path once real uploads land.
const long MaxUploadBytes = 25L * 1024 * 1024;

const string OwnerName = "owner";
const int MaxFailedLoginsPerWindow = 5;

// Temporary: plain password in source, for local debugging only. Must move back to a
// hash in configuration before anything is deployed.
const string OwnerPassword = "baba";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "kb.auth";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;

        // Cookie auth defaults to redirecting a browser to a login page; fetch() would
        // follow that 302 and read HTML as success. An API has to answer with the status.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

// Driven by hand rather than by the rate-limiting middleware: the middleware would spend
// an attempt on every request, including the successful sign-in that ends the guessing.
var failedLoginLimiter = PartitionedRateLimiter.Create<string, string>(client =>
    RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = MaxFailedLoginsPerWindow,
        Window = TimeSpan.FromMinutes(1),
    }));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // Skipped in Development on purpose: the Vite proxy forwards to http://localhost:5244,
    // and a 307 to the HTTPS endpoint would break it on the dev certificate.
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

// Created eagerly so concurrent first uploads cannot race on it.
var assetsDirectory = Path.Combine(app.Environment.ContentRootPath, "data", "assets");
Directory.CreateDirectory(assetsDirectory);

app.MapPost("/api/auth/login", async (LoginRequest request, HttpContext httpContext) =>
{
    var client = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // Statistics rather than a zero-permit probe: acquiring 0 permits always succeeds,
    // so it cannot answer whether the window is exhausted. Checked before the password is
    // verified, so a client that ran out of attempts gets no guessing feedback at all.
    if (failedLoginLimiter.GetStatistics(client) is { CurrentAvailablePermits: < 1 })
    {
        return Results.Json(
            new { error = "Too many failed attempts. Wait a minute and try again." },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    if (!string.Equals(request.Password, OwnerPassword, StringComparison.Ordinal))
    {
        failedLoginLimiter.AttemptAcquire(client).Dispose();

        return Results.Json(
            new { error = "Invalid password." },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, OwnerName)],
        CookieAuthenticationDefaults.AuthenticationScheme);

    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));

    return Results.Ok(new CurrentUserResponse(OwnerName));
})
.WithName("Login")
.WithOpenApi();

app.MapPost("/api/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
})
.RequireAuthorization()
.WithName("Logout")
.WithOpenApi();

app.MapGet("/api/auth/me", (ClaimsPrincipal user) =>
    Results.Ok(new CurrentUserResponse(user.Identity?.Name ?? OwnerName)))
.RequireAuthorization()
.WithName("GetCurrentUser")
.WithOpenApi();

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
.RequireAuthorization()
.WithName("UploadNoteAsset")
.WithOpenApi()
// Minimal-API form binding opts into antiforgery validation; SameSite=Lax already keeps
// the auth cookie off cross-site posts, so the token flow is not wired up yet.
.DisableAntiforgery();

app.Run();

record LoginRequest(string? Password);

record CurrentUserResponse(string Name);

record UploadedAssetResponse(
    string Id,
    string StoredFileName,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAtUtc);
