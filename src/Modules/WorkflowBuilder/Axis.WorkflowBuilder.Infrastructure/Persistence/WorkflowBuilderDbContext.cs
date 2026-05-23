using Axis.Shared.Application.Tenancy;
using Axis.Shared.Infrastructure.Persistence;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Axis.WorkflowBuilder.Infrastructure.Persistence;

internal sealed class WorkflowBuilderDbContext(
    DbContextOptions<WorkflowBuilderDbContext> options,
    ITenantContext tenantContext) : DbContext(options)
{
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Per ADR-017: interceptor wiring inlined per module.
        optionsBuilder.AddInterceptors(new TenantSchemaInterceptor(tenantContext));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WorkflowDefinitionConfiguration());
    }
}
