using System.Security.Claims;
using Backend.Controllers.Auth.Services;
using Backend.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace Backend.Controllers.Auth.Configuration;

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

        // Singleton because the limiter *is* the state: a per-request instance would start every
        // client back at a full window and the limit would never trigger.
        services.AddSingleton<ILoginAttemptLimiter, LoginAttemptLimiter>();

        // The session cookie is encrypted with Data Protection keys, and their default home is
        // the filesystem the app runs on — which in a container is thrown away on every deploy,
        // signing everyone out. In the database they outlive the container.
        services.AddDataProtection().PersistKeysToDbContext<KnowledgeBaseDbContext>();

        // Bound a second time by hand: the cookie scheme is configured while the container is
        // still being built, so IOptions<AuthOptions> cannot be resolved yet.
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

        // Registered only when the credentials exist. The Google handler inspects its callback
        // path on every request, so the authentication middleware builds it every time — and the
        // OAuth options validator throws on an empty ClientId. An unconfigured scheme would take
        // the whole API down, not just sign-in.
        if (auth.Google.IsConfigured)
        {
            authentication.AddGoogle(options =>
            {
                options.ClientId = auth.Google.ClientId;
                options.ClientSecret = auth.Google.ClientSecret;

                // Google is only a source of identity: the session it produces is the same
                // cookie the password login issues, so /me, logout and [Authorize] stay untouched.
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

                // Not the default /signin-google: in dev the Vite proxy forwards only /api, and
                // the callback would land in the SPA instead of the backend.
                options.CallbackPath = GoogleCallbackPath;

                // Defaults are SameSite=None + Secure, which together demand https - so the
                // flow would break on plain http, both on a dev machine and from a phone on the
                // LAN. Google comes back through a top-level navigation, so Lax still carries
                // the correlation cookie, and SameAsRequest keeps it Secure wherever https is.
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                options.Events.OnTicketReceived = context =>
                {
                    if (auth.Google.Allows(context.Principal?.FindFirst(ClaimTypes.Email)?.Value))
                    {
                        return Task.CompletedTask;
                    }

                    // HandleResponse stops the handler before it signs anyone in; Fail would
                    // surface as a 500 instead of an answer the SPA can read.
                    context.HandleResponse();
                    context.Response.Redirect($"/?authError={NotAllowedError}");

                    return Task.CompletedTask;
                };

                // Covers a denied consent screen and a lost correlation cookie; the default
                // rethrows and the user lands on an error page instead of the sign-in form.
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
