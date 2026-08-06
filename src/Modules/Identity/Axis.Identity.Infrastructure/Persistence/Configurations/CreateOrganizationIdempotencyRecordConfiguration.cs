using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class CreateOrganizationIdempotencyRecordConfiguration : IEntityTypeConfiguration<CreateOrganizationIdempotencyRecordEntity>
{
    public void Configure(EntityTypeBuilder<CreateOrganizationIdempotencyRecordEntity> builder)
    {
        builder.ToTable("create_organization_idempotency");
        builder.HasKey(x => x.ScopedKey);
        builder.Property(x => x.ScopedKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(128);
        builder.Property(x => x.CanonicalRequest)
            .HasColumnName("canonical_request")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id");
        builder.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}
