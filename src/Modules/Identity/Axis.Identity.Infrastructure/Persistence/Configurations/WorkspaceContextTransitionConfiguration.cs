using Axis.Identity.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class WorkspaceContextTransitionConfiguration
    : IEntityTypeConfiguration<WorkspaceContextTransition>
{
    public void Configure(EntityTypeBuilder<WorkspaceContextTransition> builder)
    {
        builder.ToTable("workspace_context_transitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.SourceWorkspaceId).HasColumnName("source_workspace_id");
        builder.Property(x => x.TargetWorkspaceId).HasColumnName("target_workspace_id");
        builder.Property(x => x.SourceCorrelation).HasColumnName("source_correlation").HasMaxLength(WorkspaceContextTransition.CorrelationMaximumLength);
        builder.Property(x => x.TargetCorrelation).HasColumnName("target_correlation").HasMaxLength(WorkspaceContextTransition.CorrelationMaximumLength);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.RetainUntil).HasColumnName("retain_until");
        builder.Property(x => x.TerminalAt).HasColumnName("terminal_at");
        builder.Property(x => x.AuditProjectionConfirmedAt).HasColumnName("audit_projection_confirmed_at");
        builder.Property(x => x.RedisCleanupCompletedAt).HasColumnName("redis_cleanup_completed_at");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();

        builder.HasIndex(x => x.SourceCorrelation).IsUnique();
        builder.HasIndex(x => x.TargetCorrelation).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.SourceCorrelation });
        builder.HasIndex(x => new { x.Status, x.ExpiresAt });

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(x => x.SourceWorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(x => x.TargetWorkspaceId).OnDelete(DeleteBehavior.Restrict);
    }
}
