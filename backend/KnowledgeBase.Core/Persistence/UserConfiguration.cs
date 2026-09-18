using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id).HasMaxLength(32);
        builder.Property(user => user.Email).HasMaxLength(320);

        builder.HasIndex(user => user.Email).IsUnique();

        builder.HasData(new UserAccount
        {
            Id = UserAccount.OwnerId,
            CreatedAtUtc = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}

internal sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        builder.HasKey(preference => new { preference.UserId, preference.Key });

        builder.Property(preference => preference.UserId).HasMaxLength(32);
        builder.Property(preference => preference.Key).HasMaxLength(100);
        builder.Property(preference => preference.ValueJson).HasColumnType("jsonb");

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(preference => preference.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
