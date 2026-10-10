using Microsoft.EntityFrameworkCore;

namespace Axis.Processes.Storage;

/// <summary>
/// The processes module's tables in a tenant database: the work that workers claim and run. The
/// caller supplies the connection to the tenant database.
/// </summary>
public sealed class ProcessesDbContext(DbContextOptions<ProcessesDbContext> options) : DbContext(options)
{
    public const string Schema = "axis";

    /// <summary>
    /// Each module context in the <c>axis</c> schema keeps its own migrations history table, so
    /// their histories do not collide.
    /// </summary>
    public const string MigrationsHistoryTable = "__processes_migrations";

    public DbSet<WorkItemRow> WorkItems => Set<WorkItemRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<WorkItemRow>(item =>
        {
            item.ToTable("process_work_items");
            item.HasKey(i => i.Id).HasName("pk_process_work_items");
            item.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
            item.Property(i => i.TenantId).HasColumnName("tenant_id");
            item.Property(i => i.Kind).HasColumnName("kind");
            item.Property(i => i.DueAt).HasColumnName("due_at");
            item.Property(i => i.LeaseExpiresAt).HasColumnName("lease_expires_at");
            item.Property(i => i.ClaimToken).HasColumnName("claim_token");
            item.HasIndex(i => i.DueAt).HasDatabaseName("ix_process_work_items_due_at");
        });
    }
}
