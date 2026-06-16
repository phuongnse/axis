using Axis.Identity.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("Tenant_memberships");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(m => m.tenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(m => m.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(m => new { m.UserId, m.tenantId }).IsUnique();
        builder.HasIndex(m => m.tenantId);

        builder.HasMany(m => m.Roles)
            .WithOne()
            .HasForeignKey(r => r.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(m => m.Roles)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(m => m.RoleIds);
    }
}

internal sealed class TenantMembershipRoleConfiguration : IEntityTypeConfiguration<TenantMembershipRole>
{
    public void Configure(EntityTypeBuilder<TenantMembershipRole> builder)
    {
        builder.ToTable("Tenant_membership_roles");
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
