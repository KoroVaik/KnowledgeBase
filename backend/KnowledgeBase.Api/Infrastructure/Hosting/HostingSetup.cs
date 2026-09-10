using Microsoft.AspNetCore.HttpOverrides;

namespace KnowledgeBase.Api.Infrastructure.Hosting;

public static class HostingSetup
{
    // Render picks the port at runtime and treats a process listening anywhere else as dead.
    // Unset locally, so the launch profile keeps deciding.
    public static WebApplicationBuilder UseAssignedPort(this WebApplicationBuilder builder)
    {
        var assignedPort = Environment.GetEnvironmentVariable("PORT");
        if (!string.IsNullOrWhiteSpace(assignedPort))
        {
            builder.WebHost.UseUrls($"http://*:{assignedPort}");
        }

        return builder;
    }

    public static IServiceCollection AddProxyAwareHosting(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Render's proxy address is not loopback, so the default known-proxy list would
            // drop the headers unread.
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        // Without an explicit port the redirect middleware passes everything through silently.
        services.AddHttpsRedirection(options => options.HttpsPort = 443);

        return services;
    }
}
