using Axis.Solutions.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Solutions.Infrastructure.Persistence.Configurations;

internal sealed class SolutionsAuditOutboxRecordConfiguration : IEntityTypeConfiguration<SolutionsAuditOutboxRecord>
{
    public void Configure(EntityTypeBuilder<SolutionsAuditOutboxRecord> builder)
    {
        builder.ToTable("solutions_audit_outbox"); builder.HasKey(x => x.EventId);
        builder.Property(x => x.EventId).HasColumnName("event_id").ValueGeneratedNever();
        builder.Property(x => x.ActorKind).HasColumnName("actor_kind").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ActorId).HasColumnName("actor_id");
        builder.Property(x => x.SubjectId).HasColumnName("subject_id");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(120).IsRequired();
        builder.Property(x => x.OriginatingSubjectKind).HasColumnName("originating_subject_kind").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(120).IsRequired();
        builder.Property(x => x.WorkspaceId).HasColumnName("workspace_id"); builder.Property(x => x.SolutionVersionId).HasColumnName("solution_version_id");
        builder.Property(x => x.InstallationId).HasColumnName("installation_id"); builder.Property(x => x.OperationId).HasColumnName("operation_id");
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32).IsRequired(); builder.Property(x => x.ProblemCode).HasColumnName("problem_code").HasMaxLength(200);
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired(); builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired(); builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(x => x.LastAttemptAt).HasColumnName("last_attempt_at"); builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.LeaseId).HasColumnName("lease_id"); builder.Property(x => x.LeaseUntil).HasColumnName("lease_until");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(200); builder.Property(x => x.DeliveredAt).HasColumnName("delivered_at");
        builder.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt });
    }
}
