using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");

        builder.Property(o => o.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(o => o.Slug)
            .HasColumnName("slug")
            .HasMaxLength(63)
            .IsRequired()
            .HasConversion(new ValueConverter<OrganizationSlug, string>(
                s => s.Value,
                s => OrganizationSlug.Create(s).Value!));

        builder.HasIndex(o => o.Slug).IsUnique();

        builder.Property(o => o.OwnerEmail)
            .HasColumnName("owner_email")
            .HasMaxLength(320)
            .IsRequired()
            .HasConversion(new ValueConverter<Email, string>(
                e => e.Value,
                s => Email.Create(s).Value!));

        builder.Property(o => o.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(o => o.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(o => o.SubscriptionPlanId)
            .HasColumnName("subscription_plan_id")
            .HasDefaultValue(WellKnownSubscriptionPlans.FreeId)
            .IsRequired();

        builder.HasOne<SubscriptionPlan>()
            .WithMany()
            .HasForeignKey(o => o.SubscriptionPlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
