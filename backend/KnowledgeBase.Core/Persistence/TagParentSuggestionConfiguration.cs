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
