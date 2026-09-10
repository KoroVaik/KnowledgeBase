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
        builder.Property(note => note.Category).HasMaxLength(100);
        builder.Property(note => note.SourceAssetId).HasMaxLength(32);
        builder.Property(note => note.SourceFileName).HasMaxLength(255);

        // Binned notes are invisible to every query that does not ask for them by name, so the
        // listing, the title list handed to the model and the joins in the asset listing all
        // skip them without knowing the bin exists.
        builder.HasQueryFilter(note => note.DeletedAtUtc == null);

        builder.HasIndex(note => note.Category);

        // Links resolve by title, so two live notes sharing one make resolution a coin flip.
        // Partial, because a binned note keeps its title on screen but stops occupying it.
        builder.HasIndex(note => note.Title).IsUnique().HasFilter("\"DeletedAtUtc\" IS NULL");

        // The row outlives the upload only as an entry in the bin: deleting the file bins the
        // note, and this just clears the pointer so the file name shown comes from the copy.
        builder.HasOne<AssetRecord>()
            .WithMany()
            .HasForeignKey(note => note.SourceAssetId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
