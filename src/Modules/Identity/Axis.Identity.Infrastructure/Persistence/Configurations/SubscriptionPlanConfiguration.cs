using Axis.Identity.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.ToTable("subscription_plans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(p => p.Slug).HasColumnName("slug").HasMaxLength(50).IsRequired();
        builder.HasIndex(p => p.Slug).IsUnique();

        builder.Property(p => p.MonthlyPriceCents).HasColumnName("monthly_price_cents").IsRequired();
        builder.Property(p => p.MaxWorkflows).HasColumnName("max_workflows");
        builder.Property(p => p.MaxExecutionsPerMonth).HasColumnName("max_executions_per_month");
        builder.Property(p => p.MaxUsers).HasColumnName("max_users");
        builder.Property(p => p.MaxStorageMegabytes).HasColumnName("max_storage_megabytes");
        builder.Property(p => p.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(p => p.IsAvailableForNewSignups).HasColumnName("is_available_for_new_signups").IsRequired();
    }
}
