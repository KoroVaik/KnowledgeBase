using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Persistence;

public sealed class KnowledgeBaseDbContext(DbContextOptions<KnowledgeBaseDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<AssetRecord> Assets => Set<AssetRecord>();

    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<NoteLink> NoteLinks => Set<NoteLink>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KnowledgeBaseDbContext).Assembly);
}
