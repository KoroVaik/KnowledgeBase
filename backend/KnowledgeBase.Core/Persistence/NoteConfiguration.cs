using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.HasKey(note => note.Id);

        builder.Property(note => note.Id).HasMaxLength(32);
        builder.Property(note => note.Title).HasMaxLength(200);
        builder.Property(note => note.Category).HasMaxLength(100);
        builder.Property(note => note.SourceAssetId).HasMaxLength(32);
        builder.Property(note => note.SourceFileName).HasMaxLength(255);

        builder.HasIndex(note => note.Category);

        // The note outlives the upload it came from: dropping the asset just clears the link.
        builder.HasOne<AssetRecord>()
            .WithMany()
            .HasForeignKey(note => note.SourceAssetId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
