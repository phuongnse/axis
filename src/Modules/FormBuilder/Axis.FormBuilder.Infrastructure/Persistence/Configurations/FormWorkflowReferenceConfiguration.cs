using Axis.FormBuilder.Domain.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.FormBuilder.Infrastructure.Persistence.Configurations;

internal sealed class FormWorkflowReferenceConfiguration : IEntityTypeConfiguration<FormWorkflowReference>
{
    public void Configure(EntityTypeBuilder<FormWorkflowReference> builder)
    {
        builder.ToTable("form_workflow_references");
        builder.HasKey(r => new { r.WorkflowId, r.FormId });
        builder.Property(r => r.WorkflowId).HasColumnName("workflow_id");
        builder.Property(r => r.FormId).HasColumnName("form_id");
        builder.Property(r => r.tenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(r => r.IsActive).HasColumnName("is_active").IsRequired();
        builder.HasIndex(r => r.FormId);
    }
}
