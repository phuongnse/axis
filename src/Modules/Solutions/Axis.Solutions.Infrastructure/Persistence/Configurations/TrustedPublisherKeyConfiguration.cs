using Axis.Solutions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Solutions.Infrastructure.Persistence.Configurations;

internal sealed class TrustedPublisherKeyConfiguration : IEntityTypeConfiguration<TrustedPublisherKey>
{
    public void Configure(EntityTypeBuilder<TrustedPublisherKey> builder)
    {
        builder.ToTable("trusted_publisher_keys"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.PublisherId).HasColumnName("publisher_id").HasMaxLength(63).IsRequired();
        builder.Property(x => x.KeyId).HasColumnName("key_id").HasMaxLength(63).IsRequired();
        builder.Property(x => x.SpkiSha256).HasColumnName("spki_sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.PublicKeyPem).HasColumnName("public_key_pem").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ConfigurationRevision).HasColumnName("configuration_revision").IsRequired();
        builder.Property(x => x.IsTombstone).HasColumnName("is_tombstone").IsRequired();
        builder.HasIndex(x => new { x.PublisherId, x.KeyId }).IsUnique();
    }
}
