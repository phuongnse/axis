using Axis.Solutions.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Solutions.Infrastructure.Persistence.Configurations;

internal sealed class SolutionComponentRecordConfiguration : IEntityTypeConfiguration<SolutionComponentRecord>
{
    public void Configure(EntityTypeBuilder<SolutionComponentRecord> builder)
    {
        builder.ToTable("solution_components"); builder.HasKey(x => new { x.SolutionVersionId, x.Type, x.Key });
        builder.Property(x => x.SolutionVersionId).HasColumnName("solution_version_id");
        builder.Property(x => x.Type).HasColumnName("component_type").HasMaxLength(120);
        builder.Property(x => x.Key).HasColumnName("component_key").HasMaxLength(200);
        builder.Property(x => x.Sha256).HasColumnName("component_sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Content).HasColumnName("content").HasColumnType("bytea").IsRequired();
        builder.Property(x => x.DependsOnJson).HasColumnName("depends_on").HasColumnType("jsonb").IsRequired();
        builder.HasOne<Axis.Solutions.Domain.SolutionVersion>().WithMany().HasForeignKey(x => x.SolutionVersionId).OnDelete(DeleteBehavior.Cascade);
    }
}
