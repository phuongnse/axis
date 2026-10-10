using Axis.Processes.Instances;
using Microsoft.EntityFrameworkCore;

namespace Axis.Processes.Storage;

/// <summary>
/// The processes module's tables in a tenant database: the process instances, the receipts of
/// starts that carried an <c>Idempotency-Key</c>, and the work that workers claim and run. The
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

    public DbSet<ProcessInstanceRow> Instances => Set<ProcessInstanceRow>();

    public DbSet<ProcessStartReceiptRow> StartReceipts => Set<ProcessStartReceiptRow>();

    public DbSet<WorkItemRow> WorkItems => Set<WorkItemRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ProcessInstanceRow>(instance =>
        {
            instance.ToTable("process_instances");
            instance.HasKey(i => i.Id).HasName("pk_process_instances");
            instance.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
            instance.Property(i => i.ApplicationId).HasColumnName("application_id");
            instance.Property(i => i.ProcessId).HasColumnName("process_id");
            instance.Property(i => i.SubjectEntityId).HasColumnName("subject_entity_id");
            instance.Property(i => i.SubjectId).HasColumnName("subject_id");
            instance.Property(i => i.ReleaseId).HasColumnName("release_id");
            instance.Property(i => i.State).HasColumnName("state");
            instance.Property(i => i.Revision).HasColumnName("revision");
            instance.Property(i => i.Step).HasColumnName("step");
            instance.Property(i => i.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

            // One start per submission: a record has at most one running or waiting instance of a process.
            instance.HasIndex(i => new { i.ApplicationId, i.ProcessId, i.SubjectId })
                .IsUnique()
                .HasFilter("state IN ('running', 'waiting')")
                .HasDatabaseName(ProcessStarts.ActiveSubjectIndex);
        });

        modelBuilder.Entity<ProcessStartReceiptRow>(receipt =>
        {
            receipt.ToTable("process_start_receipts");
            receipt.HasKey(r => new { r.ApplicationId, r.ProcessId, r.Key }).HasName("pk_process_start_receipts");
            receipt.Property(r => r.ApplicationId).HasColumnName("application_id");
            receipt.Property(r => r.ProcessId).HasColumnName("process_id");
            receipt.Property(r => r.Key).HasColumnName("key");
            receipt.Property(r => r.SubjectId).HasColumnName("subject_id");
            receipt.Property(r => r.InstanceId).HasColumnName("instance_id");
            receipt.Property(r => r.StatusCode).HasColumnName("status_code");
            receipt.Property(r => r.Body).HasColumnName("body").HasColumnType("jsonb");
            receipt.Property(r => r.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        });

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
            item.Property(i => i.ProcessInstanceId).HasColumnName("process_instance_id");
            item.HasIndex(i => i.DueAt).HasDatabaseName("ix_process_work_items_due_at");
            item.HasIndex(i => i.ProcessInstanceId).HasDatabaseName("ix_process_work_items_process_instance_id");
            item.HasOne<ProcessInstanceRow>()
                .WithMany()
                .HasForeignKey(i => i.ProcessInstanceId)
                .HasConstraintName("fk_process_work_items_process_instances_process_instance_id")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
