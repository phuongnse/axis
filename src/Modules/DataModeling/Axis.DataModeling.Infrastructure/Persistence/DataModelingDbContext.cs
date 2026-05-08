using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Infrastructure.Persistence.Configurations;
using Axis.Shared.Application.Tenancy;
using Axis.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Axis.DataModeling.Infrastructure.Persistence;

public sealed class DataModelingDbContext(
    DbContextOptions<DataModelingDbContext> options,
    ITenantContext tenantContext)
    : AxisDbContext(options, tenantContext)
{
    public DbSet<DataModel> DataModels => Set<DataModel>();
    public DbSet<DataClass> DataClasses => Set<DataClass>();
    public DbSet<DataRecord> DataRecords => Set<DataRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new DataModelConfiguration());
        modelBuilder.ApplyConfiguration(new DataClassConfiguration());
        modelBuilder.ApplyConfiguration(new DataRecordConfiguration());
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // EF Core uses reference equality for List<T> snapshots, so in-place mutations to
        // JSONB-backed fields are not detected automatically. Mark them modified explicitly.
        MarkJsonbFieldsModified<DataModel>();
        MarkJsonbFieldsModified<DataClass>();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void MarkJsonbFieldsModified<T>() where T : class
    {
        foreach (var entry in ChangeTracker.Entries<T>()
            .Where(e => e.State == EntityState.Modified))
        {
            entry.Property("_fields").IsModified = true;
        }
    }
}
