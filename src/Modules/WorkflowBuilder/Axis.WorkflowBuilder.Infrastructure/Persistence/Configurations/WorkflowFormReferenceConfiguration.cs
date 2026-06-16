using Axis.WorkflowBuilder.Domain.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.WorkflowBuilder.Infrastructure.Persistence.Configurations;

internal sealed class WorkflowFormReferenceConfiguration : IEntityTypeConfiguration<WorkflowFormReference>
{
    public void Configure(EntityTypeBuilder<WorkflowFormReference> builder)
    {
        builder.ToTable("workflow_form_references");
        builder.HasKey(r => new { r.WorkflowId, r.StepId });

        builder.Property(r => r.WorkflowId).HasColumnName("workflow_id").IsRequired();
        builder.Property(r => r.StepId).HasColumnName("step_id").IsRequired();
        builder.Property(r => r.FormId).HasColumnName("form_id").IsRequired();
        builder.Property(r => r.TeamAccountId).HasColumnName("team_account_id").IsRequired();
        builder.Property(r => r.IsBroken).HasColumnName("is_broken").IsRequired();

        builder.HasIndex(r => r.FormId);
        builder.HasIndex(r => new { r.TeamAccountId, r.FormId });
    }
}
