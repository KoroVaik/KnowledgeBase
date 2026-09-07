using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Backend.Infrastructure.Persistence;

public sealed class KnowledgeBaseDbContext : DbContext, IDataProtectionKeyContext
{
    public KnowledgeBaseDbContext(DbContextOptions<KnowledgeBaseDbContext> options)
        : base(options)
    {
    }

    public DbSet<AssetRecord> Assets => Set<AssetRecord>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var assets = modelBuilder.Entity<AssetRecord>();

        assets.HasKey(asset => asset.Id);

        assets.HasIndex(asset => asset.StoredFileName).IsUnique();
    }
}
