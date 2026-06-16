using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class WorkspacePlanChangeLogConfiguration : IEntityTypeConfiguration<WorkspacePlanChangeLog>
{
    public void Configure(EntityTypeBuilder<WorkspacePlanChangeLog> builder)
    {
        builder.ToTable("Workspace_plan_change_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        builder.Property(x => x.PreviousPlanId).HasColumnName("previous_plan_id").IsRequired();
        builder.Property(x => x.NewPlanId).HasColumnName("new_plan_id").IsRequired();
        builder.Property(x => x.ChangedByUserId).HasColumnName("changed_by_user_id").IsRequired();
        builder.Property(x => x.ChangedAt).HasColumnName("changed_at").IsRequired();
        builder.HasIndex(x => x.WorkspaceId);

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<SubscriptionPlan>()
            .WithMany()
            .HasForeignKey(x => x.PreviousPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SubscriptionPlan>()
            .WithMany()
            .HasForeignKey(x => x.NewPlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
