using Axis.WorkflowBuilder.Domain.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.WorkflowBuilder.Infrastructure.Persistence.Configurations;

internal sealed class WorkflowModelReferenceConfiguration : IEntityTypeConfiguration<WorkflowModelReference>
{
    public void Configure(EntityTypeBuilder<WorkflowModelReference> builder)
    {
        builder.ToTable("workflow_model_references");
        builder.HasKey(r => r.WorkflowId);

        builder.Property(r => r.WorkflowId).HasColumnName("workflow_id").IsRequired();
        builder.Property(r => r.ModelId).HasColumnName("model_id").IsRequired();
        builder.Property(r => r.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(r => r.IsBroken).HasColumnName("is_broken").IsRequired();

        builder.HasIndex(r => r.ModelId);
        builder.HasIndex(r => new { r.OrganizationId, r.ModelId });
    }
}
