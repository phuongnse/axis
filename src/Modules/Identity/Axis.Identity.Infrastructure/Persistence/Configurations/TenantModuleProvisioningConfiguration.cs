using Axis.Identity.Domain.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class TenantModuleProvisioningConfiguration : IEntityTypeConfiguration<TenantModuleProvisioning>
{
    public void Configure(EntityTypeBuilder<TenantModuleProvisioning> builder)
    {
        builder.ToTable("tenant_module_provisions");
        builder.HasKey(p => new { p.tenantId, p.Module });

        builder.Property(p => p.tenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(p => p.Module).HasColumnName("module").HasMaxLength(64).IsRequired();
        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();
        builder.Property(p => p.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(p => p.LastError).HasColumnName("last_error").HasMaxLength(2000);
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Ignore(p => p.Id);
    }
}
