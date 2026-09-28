using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KnowledgeBase.Core.Persistence;

// Lets `dotnet ef migrations add` run with Core as its own startup project, for when the API
// cannot serve there - typically a running dev instance locking its output folder. Migrations
// never open a connection, so a placeholder connection string is enough.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<KnowledgeBaseDbContext>
{
    public KnowledgeBaseDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Database=knowledgebase;Username=knowledgebase;Password=knowledgebase";
        var options = new DbContextOptionsBuilder<KnowledgeBaseDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new KnowledgeBaseDbContext(options);
    }
}
