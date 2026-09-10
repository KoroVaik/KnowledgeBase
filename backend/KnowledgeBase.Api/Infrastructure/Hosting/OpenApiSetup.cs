using System.Reflection;
using Microsoft.OpenApi.Models;

namespace KnowledgeBase.Api.Infrastructure.Hosting;

public static class OpenApiSetup
{
    public static IServiceCollection AddOpenApiDocumentation(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Knowledge Base API", Version = "v1" });

            // The compiler writes the /// texts into KnowledgeBase.Api.xml next to the assembly;
            // without this line Swagger has the routes but none of the descriptions.
            var documentation = Path.Combine(
                AppContext.BaseDirectory,
                $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");

            if (File.Exists(documentation))
            {
                options.IncludeXmlComments(documentation);
            }
        });

        return services;
    }
}
