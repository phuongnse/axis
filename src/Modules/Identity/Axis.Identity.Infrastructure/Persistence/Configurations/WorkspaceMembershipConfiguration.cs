using Axis.Identity.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class WorkspaceMembershipConfiguration : IEntityTypeConfiguration<WorkspaceMembership>
{
    public void Configure(EntityTypeBuilder<WorkspaceMembership> builder)
    {
        builder.ToTable("Workspace_memberships");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(m => m.workspaceId)
            .HasColumnName("workspace_id")
            .IsRequired();

        builder.Property(m => m.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(m => new { m.UserId, m.workspaceId }).IsUnique();
        builder.HasIndex(m => m.workspaceId);

        builder.HasMany(m => m.Roles)
            .WithOne()
            .HasForeignKey(r => r.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(m => m.Roles)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(m => m.RoleIds);
    }
}

internal sealed class WorkspaceMembershipRoleConfiguration : IEntityTypeConfiguration<WorkspaceMembershipRole>
{
    public void Configure(EntityTypeBuilder<WorkspaceMembershipRole> builder)
    {
        builder.ToTable("Workspace_membership_roles");
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
