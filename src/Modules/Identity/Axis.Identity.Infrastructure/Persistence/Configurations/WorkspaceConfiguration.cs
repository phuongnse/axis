using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("workspaces", table => table.HasCheckConstraint(
            "CK_workspaces_type_organization",
            "(type = 'Personal' AND organization_id IS NULL) OR " +
            "(type = 'Organization' AND organization_id IS NOT NULL)"));
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");

        builder.Property(o => o.Name)
            .HasColumnName("name")
            .HasMaxLength(Workspace.MaxNameLength)
            .IsRequired();

        builder.Property(o => o.Slug)
            .HasColumnName("slug")
            .HasMaxLength(63)
            .IsRequired()
            .HasConversion(new ValueConverter<WorkspaceSlug, string>(
                s => s.Value,
                s => WorkspaceSlug.Create(s).Value!));

        builder.HasIndex(o => o.Slug).IsUnique();

        builder.Property(o => o.OrganizationId).HasColumnName("organization_id");

        builder.Property(o => o.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .IsRequired();

        builder.HasIndex(o => o.OrganizationId);
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(o => o.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

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

        builder.Property(o => o.Revision).HasColumnName("revision").IsConcurrencyToken();
    }
}
