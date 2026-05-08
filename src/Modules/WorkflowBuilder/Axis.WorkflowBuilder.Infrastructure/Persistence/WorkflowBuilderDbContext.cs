using Axis.Shared.Application.Tenancy;
using Axis.Shared.Infrastructure.Persistence;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Axis.WorkflowBuilder.Infrastructure.Persistence;

internal sealed class WorkflowBuilderDbContext(
    DbContextOptions<WorkflowBuilderDbContext> options,
    ITenantContext tenantContext)
    : AxisDbContext(options, tenantContext)
{
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new WorkflowDefinitionConfiguration());
    }
}
