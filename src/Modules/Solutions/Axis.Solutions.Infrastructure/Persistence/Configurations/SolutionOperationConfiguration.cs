using Axis.Solutions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Solutions.Infrastructure.Persistence.Configurations;

internal sealed class SolutionOperationConfiguration : IEntityTypeConfiguration<SolutionInstallationOperation>
{
    public void Configure(EntityTypeBuilder<SolutionInstallationOperation> builder)
    {
        builder.ToTable("solution_installation_operations"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        builder.Property(x => x.ActorSubjectId).HasColumnName("actor_subject_id").IsRequired();
        builder.Property(x => x.ActorSubjectKind).HasColumnName("actor_subject_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.ActorCorrelationId).HasColumnName("actor_correlation_id").HasMaxLength(120).IsRequired();
        builder.Property(x => x.InstallationId).HasColumnName("installation_id").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.LeaseEpoch).HasColumnName("lease_epoch").IsRequired();
        builder.Property(x => x.LeaseExpiresAt).HasColumnName("lease_expires_at");
        builder.Property(x => x.ProblemCode).HasColumnName("problem_code").HasMaxLength(200);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();
        builder.HasIndex(x => new { x.WorkspaceId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_solution_operations_workspace_idempotency");
        builder.HasOne<SolutionInstallation>().WithMany().HasForeignKey(x => x.InstallationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Steps).WithOne().HasForeignKey("OperationId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Steps).HasField("_steps").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SolutionStepConfiguration : IEntityTypeConfiguration<SolutionInstallationStep>
{
    public void Configure(EntityTypeBuilder<SolutionInstallationStep> builder)
    {
        builder.ToTable("solution_installation_steps"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property<Guid>("OperationId").HasColumnName("operation_id").IsRequired();
        builder.Property(x => x.Order).HasColumnName("step_order").IsRequired();
        builder.Property(x => x.Type).HasColumnName("component_type").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Key).HasColumnName("component_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Sha256).HasColumnName("component_sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ApplyingEpoch).HasColumnName("applying_epoch").IsRequired();
        builder.Property(x => x.ReclaimedEpoch).HasColumnName("reclaimed_epoch");
        builder.Property(x => x.ProblemCode).HasColumnName("problem_code").HasMaxLength(200);
        builder.HasIndex("OperationId", nameof(SolutionInstallationStep.Order)).IsUnique();
    }
}
