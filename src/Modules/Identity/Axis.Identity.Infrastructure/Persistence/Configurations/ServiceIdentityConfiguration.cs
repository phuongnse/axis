using Axis.Identity.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class ServiceIdentityConfiguration : IEntityTypeConfiguration<ServiceIdentity>
{
    public void Configure(EntityTypeBuilder<ServiceIdentity> b)
    {
        b.ToTable("service_identities"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.WorkspaceId).HasColumnName("workspace_id"); b.Property(x => x.ClientId).HasColumnName("client_id").HasMaxLength(100);
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>(); b.Property(x => x.WorkspaceGrantStatus).HasColumnName("workspace_grant_status").HasConversion<string>();
        b.Property(x => x.CreatedAt).HasColumnName("created_at"); b.Property(x => x.RevokedAt).HasColumnName("revoked_at"); b.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();
        b.HasIndex(x => x.ClientId).IsUnique(); b.HasIndex(x => x.WorkspaceId);
        b.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        b.OwnsMany(x => x.Keys, k => { k.ToTable("service_identity_keys"); k.WithOwner().HasForeignKey("service_identity_id"); k.HasKey(x => x.Id); k.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); k.Property<Guid>("service_identity_id").HasColumnName("service_identity_id"); k.Property(x => x.Kid).HasColumnName("kid").HasMaxLength(128); k.Property(x => x.Thumbprint).HasColumnName("thumbprint").HasMaxLength(128); k.Property(x => x.X).HasColumnName("x").HasMaxLength(128); k.Property(x => x.Y).HasColumnName("y").HasMaxLength(128); k.Property(x => x.Status).HasColumnName("status").HasConversion<string>(); k.Property(x => x.CreatedAt).HasColumnName("created_at"); k.Property(x => x.RevokedAt).HasColumnName("revoked_at"); k.HasIndex("service_identity_id", nameof(ServiceIdentityKey.Kid)).IsUnique(); k.HasIndex("service_identity_id", nameof(ServiceIdentityKey.Thumbprint)).IsUnique(); });
        b.OwnsMany(x => x.Tombstones, t => { t.ToTable("service_identity_key_tombstones"); t.WithOwner().HasForeignKey("service_identity_id"); t.HasKey(x => x.Id); t.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); t.Property<Guid>("service_identity_id").HasColumnName("service_identity_id"); t.Property(x => x.Kid).HasColumnName("kid").HasMaxLength(128); t.Property(x => x.Thumbprint).HasColumnName("thumbprint").HasMaxLength(128); t.Property(x => x.RevokedAt).HasColumnName("revoked_at"); t.HasIndex("service_identity_id", nameof(ServiceIdentityKeyTombstone.Kid)).IsUnique(); t.HasIndex("service_identity_id", nameof(ServiceIdentityKeyTombstone.Thumbprint)).IsUnique(); });
        b.Navigation(x => x.Keys).UsePropertyAccessMode(PropertyAccessMode.Field); b.Navigation(x => x.Tombstones).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
