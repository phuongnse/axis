using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class TeamAccountPlanChangeLogConfiguration : IEntityTypeConfiguration<TeamAccountPlanChangeLog>
{
    public void Configure(EntityTypeBuilder<TeamAccountPlanChangeLog> builder)
    {
        builder.ToTable("team_account_plan_change_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TeamAccountId).HasColumnName("team_account_id").IsRequired();
        builder.Property(x => x.PreviousPlanId).HasColumnName("previous_plan_id").IsRequired();
        builder.Property(x => x.NewPlanId).HasColumnName("new_plan_id").IsRequired();
        builder.Property(x => x.ChangedByUserId).HasColumnName("changed_by_user_id").IsRequired();
        builder.Property(x => x.ChangedAt).HasColumnName("changed_at").IsRequired();
        builder.HasIndex(x => x.TeamAccountId);

        builder.HasOne<TeamAccount>()
            .WithMany()
            .HasForeignKey(x => x.TeamAccountId)
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
