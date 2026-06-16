using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class TeamAccountRegistrationTokenConfiguration
    : IEntityTypeConfiguration<TeamAccountRegistrationToken>
{
    public void Configure(EntityTypeBuilder<TeamAccountRegistrationToken> builder)
    {
        builder.ToTable("team_account_registration_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TeamAccountId).IsRequired();
        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.TeamAccountId, t.Purpose });

        builder.HasOne<TeamAccount>()
            .WithMany()
            .HasForeignKey(t => t.TeamAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
