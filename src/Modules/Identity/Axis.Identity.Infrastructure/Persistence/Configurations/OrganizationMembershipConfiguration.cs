using Axis.Identity.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationMembershipConfiguration : IEntityTypeConfiguration<OrganizationMembership>
{
    public void Configure(EntityTypeBuilder<OrganizationMembership> builder)
    {
        builder.ToTable("organization_memberships");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(m => m.OrganizationId)
            .HasColumnName("organization_id")
            .IsRequired();

        builder.Property(m => m.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(m => new { m.UserId, m.OrganizationId }).IsUnique();
        builder.HasIndex(m => m.OrganizationId);

        builder.HasMany(m => m.Roles)
            .WithOne()
            .HasForeignKey(r => r.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(m => m.Roles)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(m => m.RoleIds);
    }
}

internal sealed class OrganizationMembershipRoleConfiguration : IEntityTypeConfiguration<OrganizationMembershipRole>
{
    public void Configure(EntityTypeBuilder<OrganizationMembershipRole> builder)
    {
        builder.ToTable("organization_membership_roles");
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
