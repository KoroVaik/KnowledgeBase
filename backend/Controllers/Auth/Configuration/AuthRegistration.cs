using Backend.Controllers.Auth.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Backend.Controllers.Auth.Configuration;

public static class AuthRegistration
{
    public static IServiceCollection AddAuthFeature(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(AuthOptions.SectionName);
        services.Configure<AuthOptions>(section);

        // Singleton because the limiter *is* the state: a per-request instance would start every
        // client back at a full window and the limit would never trigger.
        services.AddSingleton<ILoginAttemptLimiter, LoginAttemptLimiter>();

        // Bound a second time by hand: the cookie scheme is configured while the container is
        // still being built, so IOptions<AuthOptions> cannot be resolved yet.
        var auth = section.Get<AuthOptions>() ?? new AuthOptions();

        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
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

        services.AddAuthorization();

        return services;
    }
}
