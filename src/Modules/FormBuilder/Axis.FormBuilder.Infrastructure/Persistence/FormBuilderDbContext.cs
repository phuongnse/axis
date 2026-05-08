using Axis.FormBuilder.Domain.Aggregates;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new FormDefinitionConfiguration());
    }
}
