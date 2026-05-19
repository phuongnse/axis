using Axis.WorkflowEngine.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.WorkflowEngine.Infrastructure.Persistence.Configurations;

internal sealed class WorkflowActiveStatusConfiguration : IEntityTypeConfiguration<WorkflowActiveStatus>
{
    public void Configure(EntityTypeBuilder<WorkflowActiveStatus> builder)
    {
        builder.ToTable("workflow_active_statuses");
        builder.HasKey(w => w.WorkflowId);
        builder.Property(w => w.WorkflowId).HasColumnName("workflow_id");
        builder.Property(w => w.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(w => w.IsActive).HasColumnName("is_active").IsRequired();
    }
}
