using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Persistence;

public static class PersistenceRegistration
{
    private const string ConnectionName = "Database";

    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionName);

        // Fail on start, not on the first request: a missing string is a deployment mistake.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionName} is not set. Locally it comes from "
                + "appsettings.Development.json, in the cloud from the ConnectionStrings__Database "
                + "environment variable.");
        }

        // Scoped: a DbContext is not thread-safe, so it lives one HTTP request long.
        services.AddDbContext<KnowledgeBaseDbContext>(options =>
            // The managed DB sleeps when idle; without retries the request that wakes it fails.
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

        return services;
    }

    // Safe on start only because the API owns the schema (the worker never calls this).
    // IHost, not the ASP.NET types, so it stays usable from Core.
    public static IHost MigrateDatabase(this IHost app)
    {
        using var scope = app.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<KnowledgeBaseDbContext>().Database.Migrate();

        return app;
    }
}
