using Axis.Shared.Domain.Primitives;
using Axis.Solutions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Solutions.Infrastructure.Persistence.Configurations;

internal sealed class SolutionInstallationConfiguration : IEntityTypeConfiguration<SolutionInstallation>
{
    public void Configure(EntityTypeBuilder<SolutionInstallation> builder)
    {
        builder.ToTable("solution_installations"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        builder.Property(x => x.SolutionKey).HasColumnName("solution_key").HasMaxLength(63).IsRequired();
        builder.Property(x => x.SolutionVersionId).HasColumnName("solution_version_id").IsRequired();
        builder.Property(x => x.ProvisioningStatus).HasColumnName("provisioning_status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ComplianceStatus).HasColumnName("compliance_status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();
        builder.Property<ActorKind?>("CreatedByKind").HasColumnName("created_by_kind").HasConversion<string>().HasMaxLength(32);
        builder.Property<Guid?>("CreatedBySubjectId").HasColumnName("created_by_subject_id");
        builder.Property<string?>("CreatedByDisplayName").HasColumnName("created_by_display_name").HasMaxLength(ActorSnapshot.MaximumDisplayNameLength);
        builder.Property<ActorKind?>("UpdatedByKind").HasColumnName("updated_by_kind").HasConversion<string>().HasMaxLength(32);
        builder.Property<Guid?>("UpdatedBySubjectId").HasColumnName("updated_by_subject_id");
        builder.Property<string?>("UpdatedByDisplayName").HasColumnName("updated_by_display_name").HasMaxLength(ActorSnapshot.MaximumDisplayNameLength);
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.UpdatedBy);
        builder.HasIndex(x => new { x.WorkspaceId, x.SolutionKey })
            .IsUnique()
            .HasDatabaseName("ux_solution_installations_workspace_solution");
        builder.HasOne<SolutionVersion>().WithMany().HasForeignKey(x => x.SolutionVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
