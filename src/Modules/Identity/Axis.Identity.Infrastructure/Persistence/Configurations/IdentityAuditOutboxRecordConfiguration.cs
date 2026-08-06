using Axis.Audit.Contracts;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class IdentityAuditOutboxRecordConfiguration
    : IEntityTypeConfiguration<IdentityAuditOutboxRecord>
{
    public void Configure(EntityTypeBuilder<IdentityAuditOutboxRecord> builder)
    {
        builder.ToTable("identity_audit_outbox");
        builder.HasKey(x => x.EventId);
        builder.Property(x => x.EventId).HasColumnName("event_id").ValueGeneratedNever();
        builder.Property(x => x.ActorKind).HasColumnName("actor_kind").HasConversion<string>();
        builder.Property(x => x.ActorId).HasColumnName("actor_id");
        builder.Property(x => x.SubjectId).HasColumnName("subject_id");
        builder.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
        builder.Property(x => x.Action)
            .HasColumnName("action")
            .HasMaxLength(AuditEventV1Validator.MaximumCategoryLength);
        builder.Property(x => x.TargetType)
            .HasColumnName("target_type")
            .HasMaxLength(AuditEventV1Validator.MaximumCategoryLength);
        builder.Property(x => x.TargetId).HasColumnName("target_id");
        builder.Property(x => x.Outcome)
            .HasColumnName("outcome")
            .HasMaxLength(AuditEventV1Validator.MaximumCategoryLength);
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at");
        builder.Property(x => x.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(AuditEventV1Validator.MaximumCorrelationIdLength);
        builder.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        builder.Property(x => x.LastAttemptAt).HasColumnName("last_attempt_at");
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(256);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}
