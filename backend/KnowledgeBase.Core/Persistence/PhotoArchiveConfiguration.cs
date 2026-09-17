using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.HasKey(person => person.Id);
        builder.Property(person => person.Id).HasMaxLength(32);
        builder.Property(person => person.Name).HasMaxLength(160);
        builder.HasIndex(person => person.Name).IsUnique();
    }
}

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.HasKey(location => location.Id);
        builder.Property(location => location.Id).HasMaxLength(32);
        builder.Property(location => location.Name).HasMaxLength(200);
        builder.Property(location => location.Kind).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(location => new { location.Name, location.Kind }).IsUnique();
    }
}

internal sealed class ArchiveEventConfiguration : IEntityTypeConfiguration<ArchiveEvent>
{
    public void Configure(EntityTypeBuilder<ArchiveEvent> builder)
    {
        builder.HasKey(@event => @event.Id);
        builder.Property(@event => @event.Id).HasMaxLength(32);
        builder.Property(@event => @event.Title).HasMaxLength(200);
        builder.Property(@event => @event.LocationId).HasMaxLength(32);
        builder.HasOne<Location>().WithMany().HasForeignKey(@event => @event.LocationId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ArchiveEventPhotoConfiguration : IEntityTypeConfiguration<ArchiveEventPhoto>
{
    public void Configure(EntityTypeBuilder<ArchiveEventPhoto> builder)
    {
        builder.HasKey(link => new { link.EventId, link.AssetId });
        builder.Property(link => link.EventId).HasMaxLength(32);
        builder.Property(link => link.AssetId).HasMaxLength(32);
        builder.HasOne<ArchiveEvent>().WithMany().HasForeignKey(link => link.EventId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AssetRecord>().WithMany().HasForeignKey(link => link.AssetId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ArchiveEventPersonConfiguration : IEntityTypeConfiguration<ArchiveEventPerson>
{
    public void Configure(EntityTypeBuilder<ArchiveEventPerson> builder)
    {
        builder.HasKey(link => new { link.EventId, link.PersonId });
        builder.Property(link => link.EventId).HasMaxLength(32);
        builder.Property(link => link.PersonId).HasMaxLength(32);
        builder.HasOne<ArchiveEvent>().WithMany().HasForeignKey(link => link.EventId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Person>().WithMany().HasForeignKey(link => link.PersonId).OnDelete(DeleteBehavior.Cascade);
    }
}
