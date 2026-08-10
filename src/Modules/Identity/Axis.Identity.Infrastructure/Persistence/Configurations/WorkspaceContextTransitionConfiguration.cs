using Axis.Identity.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class WorkspaceContextTransitionConfiguration
    : IEntityTypeConfiguration<WorkspaceContextTransition>
{
    public void Configure(EntityTypeBuilder<WorkspaceContextTransition> builder)
    {
        builder.ToTable("workspace_context_transitions", table =>
        {
            table.HasCheckConstraint(
                "CK_transition_source_digest",
                "source_correlation_digest ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint(
                "CK_transition_target_digest",
                "target_correlation_digest ~ '^[0-9a-f]{64}$'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.SourceWorkspaceId).HasColumnName("source_workspace_id");
        builder.Property(x => x.TargetWorkspaceId).HasColumnName("target_workspace_id");
        builder.Property(x => x.TerminalAuditEventId).HasColumnName("terminal_audit_event_id");
        builder.Property(x => x.SourceCorrelationDigest).HasColumnName("source_correlation_digest").HasMaxLength(WorkspaceContextTransition.CorrelationDigestLength);
        builder.Property(x => x.TargetCorrelationDigest).HasColumnName("target_correlation_digest").HasMaxLength(WorkspaceContextTransition.CorrelationDigestLength);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.RetainUntil).HasColumnName("retain_until");
        builder.Property(x => x.TerminalAt).HasColumnName("terminal_at");
        builder.Property(x => x.AuditProjectionConfirmedAt).HasColumnName("audit_projection_confirmed_at");
        builder.Property(x => x.RedisCleanupCompletedAt).HasColumnName("redis_cleanup_completed_at");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();

        builder.HasIndex(x => x.SourceCorrelationDigest).IsUnique();
        builder.HasIndex(x => x.TargetCorrelationDigest).IsUnique();
        builder.HasIndex(x => x.TerminalAuditEventId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.ExpiresAt });

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(x => x.SourceWorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(x => x.TargetWorkspaceId).OnDelete(DeleteBehavior.Restrict);
    }
}
