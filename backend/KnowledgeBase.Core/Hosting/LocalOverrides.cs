using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace KnowledgeBase.Core.Hosting;

public static class LocalOverrides
{
    // Machine-specific settings (keys, endpoints). Added after the builder so it outranks
    // every other source; gated on Development so a stray copy near a deployed binary cannot
    // override. IHostApplicationBuilder is common to the API and worker hosts.
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
