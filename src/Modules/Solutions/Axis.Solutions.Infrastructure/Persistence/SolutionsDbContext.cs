using Axis.Solutions.Domain;
using Axis.Solutions.Infrastructure.Persistence.Configurations;
using Axis.Solutions.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Solutions.Infrastructure.Persistence;

public sealed class SolutionsDbContext(DbContextOptions<SolutionsDbContext> options) : DbContext(options)
{
    public DbSet<SolutionVersion> SolutionVersions => Set<SolutionVersion>();
    public DbSet<SolutionInstallation> SolutionInstallations => Set<SolutionInstallation>();
    public DbSet<SolutionInstallationOperation> SolutionOperations => Set<SolutionInstallationOperation>();
    public DbSet<TrustedPublisherKey> TrustedPublisherKeys => Set<TrustedPublisherKey>();
    internal DbSet<TrustedPublisherLedgerStateRecord> TrustedPublisherLedgerState => Set<TrustedPublisherLedgerStateRecord>();
    internal DbSet<SolutionComponentRecord> Components => Set<SolutionComponentRecord>();
    internal DbSet<SolutionsAuditOutboxRecord> AuditOutbox => Set<SolutionsAuditOutboxRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SolutionVersionConfiguration());
        modelBuilder.ApplyConfiguration(new SolutionInstallationConfiguration());
        modelBuilder.ApplyConfiguration(new SolutionOperationConfiguration());
        modelBuilder.ApplyConfiguration(new SolutionStepConfiguration());
        modelBuilder.ApplyConfiguration(new TrustedPublisherKeyConfiguration());
        modelBuilder.ApplyConfiguration(new TrustedPublisherLedgerStateRecordConfiguration());
        modelBuilder.ApplyConfiguration(new SolutionComponentRecordConfiguration());
        modelBuilder.ApplyConfiguration(new SolutionsAuditOutboxRecordConfiguration());
    }
}
