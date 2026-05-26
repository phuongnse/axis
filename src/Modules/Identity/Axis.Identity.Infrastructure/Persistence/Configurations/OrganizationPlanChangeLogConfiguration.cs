using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Identity.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationPlanChangeLogConfiguration : IEntityTypeConfiguration<OrganizationPlanChangeLog>
{
    public void Configure(EntityTypeBuilder<OrganizationPlanChangeLog> builder)
    {
        builder.ToTable("organization_plan_change_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(x => x.PreviousPlanId).HasColumnName("previous_plan_id").IsRequired();
        builder.Property(x => x.NewPlanId).HasColumnName("new_plan_id").IsRequired();
        builder.Property(x => x.ChangedByUserId).HasColumnName("changed_by_user_id").IsRequired();
        builder.Property(x => x.ChangedAt).HasColumnName("changed_at").IsRequired();
        builder.HasIndex(x => x.OrganizationId);
    }
}
