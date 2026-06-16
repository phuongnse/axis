using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("Workspaces");
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
            .HasConversion(new ValueConverter<WorkspaceSlug, string>(
                s => s.Value,
                s => WorkspaceSlug.Create(s).Value!));

        builder.HasIndex(o => o.Slug).IsUnique();

        builder.Property(o => o.OwnerEmail)
            .HasColumnName("owner_email")
            .HasMaxLength(320)
            .IsRequired()
            .HasConversion(new ValueConverter<Email, string>(
                e => e.Value,
                s => Email.Create(s).Value!));

        builder.Property(o => o.OwnerUserId)
            .HasColumnName("owner_user_id");

        builder.Property(o => o.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .IsRequired();

        builder.HasIndex(o => new { o.OwnerUserId, o.Type })
            .IsUnique()
            .HasFilter("owner_user_id IS NOT NULL AND type = 'Personal'");

        builder.Property(o => o.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(o => o.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(o => o.AcceptedTermsVersion)
            .HasColumnName("accepted_terms_version")
            .HasMaxLength(32);

        builder.Property(o => o.AcceptedPrivacyVersion)
            .HasColumnName("accepted_privacy_version")
            .HasMaxLength(32);

        builder.Property(o => o.LegalAcceptedAt)
            .HasColumnName("legal_accepted_at");

        builder.Property(o => o.SubscriptionPlanId)
            .HasColumnName("subscription_plan_id")
            .HasDefaultValue(WellKnownSubscriptionPlans.FreeId)
            .IsRequired();

        builder.Property(o => o.LogoUrl)
            .HasColumnName("logo_url")
            .HasMaxLength(2048);

        builder.Property(o => o.TimeZoneId)
            .HasColumnName("time_zone_id")
            .HasMaxLength(64);

        builder.Property(o => o.DefaultLanguage)
            .HasColumnName("default_language")
            .HasMaxLength(16);

        builder.Property(o => o.ScheduledHardDeleteAt)
            .HasColumnName("scheduled_hard_delete_at");

        builder.HasOne<SubscriptionPlan>()
            .WithMany()
            .HasForeignKey(o => o.SubscriptionPlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
