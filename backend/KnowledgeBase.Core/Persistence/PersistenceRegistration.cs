using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Persistence;

public static class PersistenceRegistration
{
    private const string ConnectionName = "Database";

    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionName);

        // Fail on start rather than on the first request: a missing connection string is a
        // deployment mistake, and a 500 an hour later hides it.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionName} is not set. Locally it comes from "
                + "appsettings.Development.json, in the cloud from the ConnectionStrings__Database "
                + "environment variable.");
        }

        // Scoped by default: a DbContext is not thread-safe, so it lives one HTTP request long.
        services.AddDbContext<KnowledgeBaseDbContext>(options =>
            // The managed database sleeps when idle and takes a moment to wake; without retries
            // the request that wakes it is the one that fails.
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

        return services;
    }

    // Migrating on start is safe only because one process owns the schema: the API. The worker
    // takes AddDatabase but never this - it runs against tables the API has already created.
    // Typed as IHost so it does not drag the ASP.NET types into this project.
    public static IHost MigrateDatabase(this IHost app)
    {
        using var scope = app.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<KnowledgeBaseDbContext>().Database.Migrate();

        return app;
    }
}
