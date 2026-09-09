using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace KnowledgeBase.Core.Hosting;

public static class LocalOverrides
{
    // Machine-specific settings that must not be shared: keys, endpoints, local switches.
    // Added after the builder is created, so it outranks every other source, including
    // environment variables — gated on Development so a stray copy next to a deployed binary
    // cannot quietly override the real configuration.
    //
    // IHostApplicationBuilder is the common surface of WebApplicationBuilder (API) and the
    // worker's HostApplicationBuilder, so both hosts get the exact same override layer.
    public static TBuilder UseLocalOverrides<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        if (builder.Environment.IsDevelopment())
        {
            builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
        }

        return builder;
    }
}
