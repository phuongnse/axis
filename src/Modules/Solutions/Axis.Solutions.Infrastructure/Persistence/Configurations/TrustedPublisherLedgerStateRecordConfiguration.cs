using Axis.Solutions.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Solutions.Infrastructure.Persistence.Configurations;

internal sealed class TrustedPublisherLedgerStateRecordConfiguration : IEntityTypeConfiguration<TrustedPublisherLedgerStateRecord>
{
    public void Configure(EntityTypeBuilder<TrustedPublisherLedgerStateRecord> builder)
    {
        builder.ToTable("trusted_publisher_ledger_state");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ActiveRevision).HasColumnName("active_revision").IsRequired();
        builder.Property<uint>("xmin").IsRowVersion();
    }
}
