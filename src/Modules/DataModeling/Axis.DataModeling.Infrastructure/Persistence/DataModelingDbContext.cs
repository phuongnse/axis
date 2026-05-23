using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Infrastructure.Persistence.Configurations;
using Axis.Shared.Application.Tenancy;
using Axis.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.DataModeling.Infrastructure.Persistence;

internal sealed class DataModelingDbContext(
    DbContextOptions<DataModelingDbContext> options,
    ITenantContext tenantContext) : DbContext(options)
{
    public DbSet<DataModel> DataModels => Set<DataModel>();
    public DbSet<DataClass> DataClasses => Set<DataClass>();
    public DbSet<DataRecord> DataRecords => Set<DataRecord>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Per ADR-017: interceptor wiring inlined here rather than inherited
        // from a shared AxisDbContext base. TenantSchemaInterceptor stays in
        // Axis.Shared.Infrastructure because every module needs it identically.
        optionsBuilder.AddInterceptors(new TenantSchemaInterceptor(tenantContext));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new DataModelConfiguration());
        modelBuilder.ApplyConfiguration(new DataClassConfiguration());
        modelBuilder.ApplyConfiguration(new DataRecordConfiguration());
    }
}
