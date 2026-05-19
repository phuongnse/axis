using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Infrastructure.Persistence.Configurations;
using Axis.Shared.Application.Tenancy;
using Axis.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.FormBuilder.Infrastructure.Persistence;

internal sealed class FormBuilderDbContext(
    DbContextOptions<FormBuilderDbContext> options,
    ITenantContext tenantContext)
    : AxisDbContext(options, tenantContext)
{
    public DbSet<FormDefinition> FormDefinitions => Set<FormDefinition>();
    public DbSet<FormWorkflowReference> FormWorkflowReferences => Set<FormWorkflowReference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new FormDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new FormWorkflowReferenceConfiguration());
    }
}
