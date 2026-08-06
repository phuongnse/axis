using System.Text.Json;
using Axis.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.Audit.Infrastructure.Persistence.Configurations;

internal sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    private static readonly ValueConverter<Dictionary<string, string>, string> MetadataConverter = new(
        metadata => JsonSerializer.Serialize(metadata),
        json => JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(StringComparer.Ordinal));

    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("audit_records", table => table.HasCheckConstraint(
            "CK_audit_records_actor",
            "(actor_kind IN ('Human', 'ServiceIdentity') AND actor_id IS NOT NULL) OR " +
            "(actor_kind IN ('System', 'Anonymous') AND actor_id IS NULL)"));
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(record => record.EventId).HasColumnName("event_id").IsRequired();
        builder.Property(record => record.ActorKind).HasColumnName("actor_kind").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(record => record.ActorId).HasColumnName("actor_id");
        builder.Property(record => record.SubjectId).HasColumnName("subject_id");
        builder.Property(record => record.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        builder.Property(record => record.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
        builder.Property(record => record.TargetType).HasColumnName("target_type").HasMaxLength(64).IsRequired();
        builder.Property(record => record.TargetId).HasColumnName("target_id").IsRequired();
        builder.Property(record => record.Outcome).HasColumnName("outcome").HasMaxLength(64).IsRequired();
        builder.Property(record => record.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(record => record.CorrelationId).HasColumnName("correlation_id").HasMaxLength(120).IsRequired();
        builder.Ignore(record => record.Metadata);
        builder.Property<Dictionary<string, string>>("_metadata")
            .HasColumnName("metadata")
            .HasColumnType("jsonb")
            .HasConversion(MetadataConverter)
            .IsRequired();
        builder.HasIndex(record => record.EventId).IsUnique();

        foreach (IMutableProperty property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
