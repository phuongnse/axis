using Axis.Shared.Application.Tenancy;
using Axis.Shared.Infrastructure.Persistence;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.ReadModels;
using Axis.WorkflowEngine.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Axis.WorkflowEngine.Infrastructure.Persistence;

internal sealed class WorkflowEngineDbContext(
    DbContextOptions<WorkflowEngineDbContext> options,
    ITenantContext tenantContext) : DbContext(options)
{
    public DbSet<WorkflowExecution> WorkflowExecutions => Set<WorkflowExecution>();
    public DbSet<WorkflowActiveStatus> WorkflowActiveStatuses => Set<WorkflowActiveStatus>();
    public DbSet<WorkflowSnapshot> WorkflowSnapshots => Set<WorkflowSnapshot>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Per ADR-017: interceptor wiring inlined per module.
        optionsBuilder.AddInterceptors(new TenantSchemaInterceptor(tenantContext));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ExecutionConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowActiveStatusConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowSnapshotConfiguration());
    }
}
