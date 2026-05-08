using Axis.Shared.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Axis.Shared.Infrastructure.Persistence;

/// <summary>
/// Abstract base DbContext for all module DbContexts.
/// Registers the TenantSchemaInterceptor so every connection automatically
/// targets the correct tenant's PostgreSQL schema.
/// </summary>
public abstract class AxisDbContext(
    DbContextOptions options,
    ITenantContext tenantContext) : DbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(new TenantSchemaInterceptor(tenantContext));
    }
}
