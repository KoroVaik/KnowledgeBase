using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.HasKey(note => note.Id);

        builder.Property(note => note.Id).HasMaxLength(32);
        builder.Property(note => note.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(note => note.Title).HasMaxLength(200);
        builder.Property(note => note.SourceAssetId).HasMaxLength(32);
        builder.Property(note => note.SourceFileName).HasMaxLength(255);

        // Binned notes drop out of every query that does not ask by name.
        builder.HasQueryFilter(note => note.DeletedAtUtc == null);

        // Links resolve by title; two live notes sharing one make it a coin flip. Partial - a
        // binned note keeps its title on screen but stops occupying it.
        builder.HasIndex(note => note.Title).IsUnique().HasFilter("\"DeletedAtUtc\" IS NULL");

        // Deleting the file bins the note; this just clears the pointer (name comes from the copy).
        builder.HasOne<AssetRecord>()
            .WithMany()
            .HasForeignKey(note => note.SourceAssetId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
