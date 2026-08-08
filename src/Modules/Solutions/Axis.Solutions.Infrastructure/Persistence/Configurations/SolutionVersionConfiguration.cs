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
        builder.HasIndex(x => new { x.SolutionKey, x.Version })
            .IsUnique()
            .HasDatabaseName("ux_solution_versions_identity");
    }
}
