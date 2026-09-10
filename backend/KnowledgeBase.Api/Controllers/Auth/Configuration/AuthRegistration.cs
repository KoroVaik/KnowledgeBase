using System.Security.Claims;
using KnowledgeBase.Api.Controllers.Auth.Services;
using KnowledgeBase.Core.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace KnowledgeBase.Api.Controllers.Auth.Configuration;

public static class AuthRegistration
{
    private const string GoogleCallbackPath = "/api/auth/google/callback";

    private const string NotAllowedError = "google-not-allowed";
    private const string FailedError = "google-failed";

    public static IServiceCollection AddAuthFeature(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(AuthOptions.SectionName);
        services.Configure<AuthOptions>(section);

        // Singleton: the limiter *is* the state; per-request would reset every client's window.
        services.AddSingleton<ILoginAttemptLimiter, LoginAttemptLimiter>();

        // Data Protection keys default to disk, thrown away on each container deploy (= logout).
        services.AddDataProtection().PersistKeysToDbContext<KnowledgeBaseDbContext>();

        // By hand: the cookie scheme is configured before the container is built, so IOptions
        // is not resolvable yet.
        var auth = section.Get<AuthOptions>() ?? new AuthOptions();

        var authentication = services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme);

        authentication
            .AddCookie(options =>
            {
                options.Cookie.Name = "kb.auth";
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = auth.SessionLifetime;
                options.SlidingExpiration = true;

                // Cookie auth redirects to a login page by default; fetch() would read that
                // HTML as success. An API answers with the status.
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

        // Only with credentials: the handler checks its callback on every request, and the
        // OAuth validator throws on an empty ClientId - an unconfigured scheme downs the API.
        if (auth.Google.IsConfigured)
        {
            authentication.AddGoogle(options =>
            {
                options.ClientId = auth.Google.ClientId;
                options.ClientSecret = auth.Google.ClientSecret;

                // Google is only identity: the session is the same kb.auth cookie the password
                // login issues.
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

                // Not /signin-google: the Vite proxy forwards only /api.
                options.CallbackPath = GoogleCallbackPath;

                // Defaults (SameSite=None + Secure) demand https and break on plain http (LAN
                // phone). Lax still carries the correlation cookie on Google's top-level return;
                // SameAsRequest keeps it Secure on https.
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                options.Events.OnTicketReceived = context =>
                {
                    if (auth.Google.Allows(context.Principal?.FindFirst(ClaimTypes.Email)?.Value))
                    {
                        return Task.CompletedTask;
                    }

                    // HandleResponse stops the handler before sign-in; Fail would be a 500.
                    context.HandleResponse();
                    context.Response.Redirect($"/?authError={NotAllowedError}");

                    return Task.CompletedTask;
                };

                // Denied consent or a lost correlation cookie; the default rethrows to an error page.
                options.Events.OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect($"/?authError={FailedError}");

                    return Task.CompletedTask;
                };
            });
        }

        services.AddAuthorization();

        return services;
    }
}
