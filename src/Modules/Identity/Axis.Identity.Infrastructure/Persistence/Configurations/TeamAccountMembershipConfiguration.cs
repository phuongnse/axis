using Axis.Identity.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class TeamAccountMembershipConfiguration : IEntityTypeConfiguration<TeamAccountMembership>
{
    public void Configure(EntityTypeBuilder<TeamAccountMembership> builder)
    {
        builder.ToTable("team_account_memberships");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(m => m.TeamAccountId)
            .HasColumnName("team_account_id")
            .IsRequired();

        builder.Property(m => m.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(m => new { m.UserId, m.TeamAccountId }).IsUnique();
        builder.HasIndex(m => m.TeamAccountId);

        builder.HasMany(m => m.Roles)
            .WithOne()
            .HasForeignKey(r => r.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(m => m.Roles)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(m => m.RoleIds);
    }
}

internal sealed class TeamAccountMembershipRoleConfiguration : IEntityTypeConfiguration<TeamAccountMembershipRole>
{
    public void Configure(EntityTypeBuilder<TeamAccountMembershipRole> builder)
    {
        builder.ToTable("team_account_membership_roles");
        builder.HasKey(r => new { r.MembershipId, r.RoleId });
        builder.Ignore(r => r.Id);
        builder.Property(r => r.MembershipId).HasColumnName("membership_id");
        builder.Property(r => r.RoleId).HasColumnName("role_id");
        builder.Property(r => r.AssignedAt)
            .HasColumnName("assigned_at")
            .IsRequired();
        builder.HasIndex(r => r.RoleId);
    }
}
