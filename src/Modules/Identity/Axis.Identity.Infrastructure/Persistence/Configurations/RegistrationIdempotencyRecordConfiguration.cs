using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class RegistrationIdempotencyRecordConfiguration
    : IEntityTypeConfiguration<RegistrationIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<RegistrationIdempotencyRecord> builder)
    {
        builder.ToTable("registration_idempotency");
        builder.HasKey(r => r.IdempotencyKey);
        builder.Property(r => r.IdempotencyKey).HasMaxLength(128);
        builder.Property(r => r.CreatedAt).IsRequired();
    }
}
