using Axis.Shared.Domain.Primitives;
using Axis.Solutions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Solutions.Infrastructure.Persistence.Configurations;

internal sealed class SolutionVersionConfiguration : IEntityTypeConfiguration<SolutionVersion>
{
    public void Configure(EntityTypeBuilder<SolutionVersion> builder)
    {
        builder.ToTable("solution_versions"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.SolutionKey).HasColumnName("solution_key").HasMaxLength(63).IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").HasMaxLength(64).IsRequired();
        builder.Property(x => x.PackageSha256).HasColumnName("package_sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Envelope).HasColumnName("envelope").HasColumnType("bytea").IsRequired();
        builder.Property(x => x.AxisOpenApiSha256).HasColumnName("axis_openapi_sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.PublisherId).HasColumnName("publisher_id").HasMaxLength(63).IsRequired();
        builder.Property(x => x.PublisherKeyId).HasColumnName("publisher_key_id").HasMaxLength(63).IsRequired();
        builder.Property(x => x.SourceRevision).HasColumnName("source_revision").HasMaxLength(64).IsRequired();
        builder.Property(x => x.BuildId).HasColumnName("build_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.BuiltAt).HasColumnName("built_at").IsRequired();
        builder.Property(x => x.SourceUri).HasColumnName("source_uri").HasMaxLength(2048).IsRequired();
        builder.Property(x => x.PublishedAt).HasColumnName("published_at").IsRequired();
        builder.Property<ActorKind>("CreatedByKind")
            .HasColumnName("created_by_kind")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property<Guid?>("CreatedBySubjectId").HasColumnName("created_by_subject_id");
        builder.Property<string>("CreatedByDisplayName")
            .HasColumnName("created_by_display_name")
            .HasMaxLength(ActorSnapshot.MaximumDisplayNameLength)
            .IsRequired();
        builder.Ignore(x => x.CreatedBy);
        builder.HasIndex(x => new { x.SolutionKey, x.Version })
            .IsUnique()
            .HasDatabaseName("ux_solution_versions_identity");
    }
}
