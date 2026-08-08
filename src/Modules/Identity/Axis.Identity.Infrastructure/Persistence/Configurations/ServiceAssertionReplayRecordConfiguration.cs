using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class ServiceAssertionReplayRecordConfiguration : IEntityTypeConfiguration<ServiceAssertionReplayRecord>
{ public void Configure(EntityTypeBuilder<ServiceAssertionReplayRecord> b) { b.ToTable("service_assertion_replays"); b.HasKey(x => x.Digest); b.Property(x => x.Digest).HasColumnName("digest").HasMaxLength(64); b.Property(x => x.ExpiresAt).HasColumnName("expires_at"); b.Property(x => x.CreatedAt).HasColumnName("created_at"); b.HasIndex(x => x.ExpiresAt); } }
