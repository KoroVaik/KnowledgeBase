using Microsoft.AspNetCore.HttpOverrides;

namespace KnowledgeBase.Api.Hosting;

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

            // The default known-proxy list is loopback only, and Render's proxy address is
            // neither fixed nor local — left as is, the headers would be dropped unread.
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        // Without an explicit port the redirect middleware cannot build a target and silently
        // passes everything through.
        services.AddHttpsRedirection(options => options.HttpsPort = 443);

        return services;
    }
}
