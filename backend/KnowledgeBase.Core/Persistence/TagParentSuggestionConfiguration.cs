using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class TagParentSuggestionConfiguration : IEntityTypeConfiguration<TagParentSuggestion>
{
    public void Configure(EntityTypeBuilder<TagParentSuggestion> builder)
    {
        builder.HasKey(suggestion => new { suggestion.ChildId, suggestion.ParentId });

        builder.Property(suggestion => suggestion.ChildId).HasMaxLength(32);
        builder.Property(suggestion => suggestion.ParentId).HasMaxLength(32);

        // No model-level HasDefaultValue: with Low as the enum's CLR default (0), EF would treat
        // an explicit Low the same as "unset" and silently store the DB default instead. Every
        // insert sets Confidence itself; the migration still gives the ALTER TABLE a one-time
        // default so it does not fail against any row from before this column existed.
        builder.Property(suggestion => suggestion.Confidence).HasConversion<string>().HasMaxLength(16);

        // "suggested parents of X" and "suggested children of X" - both directions are queried.
        builder.HasIndex(suggestion => suggestion.ParentId);

        builder.HasOne<Tag>()
            .WithMany()
            .HasForeignKey(suggestion => suggestion.ChildId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Tag>()
            .WithMany()
            .HasForeignKey(suggestion => suggestion.ParentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
