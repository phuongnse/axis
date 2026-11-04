using Microsoft.EntityFrameworkCore;

namespace Axis.ControlPlane.Storage;

/// <summary>
/// The <c>control_plane</c> schema of the control plane database. It holds the durable <c>jobs</c> and the append-only
/// <c>audit_records</c>: the sign-up and billing issues add the rest. The caller supplies the connection to the database.
/// </summary>
public sealed class ControlPlaneDbContext(DbContextOptions<ControlPlaneDbContext> options) : DbContext(options)
{
    public const string Schema = "control_plane";

    public const string MigrationsHistoryTable = "__control_plane_migrations";

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema));

    public DbSet<JobRow> Jobs => Set<JobRow>();

    public DbSet<AuditRecordRow> AuditRecords => Set<AuditRecordRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<JobRow>(job =>
        {
            job.ToTable("jobs", table => table.HasCheckConstraint("ck_jobs_state", "state IN ('queued', 'running', 'succeeded', 'failed')"));
            job.HasKey(j => j.Id).HasName("pk_jobs");
            job.Property(j => j.Id).HasColumnName("id").ValueGeneratedNever();
            job.Property(j => j.Kind).HasColumnName("kind");
            job.Property(j => j.Payload).HasColumnName("payload").HasColumnType("jsonb");
            job.Property(j => j.State).HasColumnName("state").HasDefaultValue("queued");
            job.Property(j => j.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            job.Property(j => j.NextAttemptAt).HasColumnName("next_attempt_at");
            job.Property(j => j.LeaseOwner).HasColumnName("lease_owner");
            job.Property(j => j.LeaseUntil).HasColumnName("lease_until");
            job.Property(j => j.LastError).HasColumnName("last_error");
            job.Property(j => j.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            job.Property(j => j.FinishedAt).HasColumnName("finished_at");
            job.HasIndex(j => j.NextAttemptAt, "ix_jobs_next_attempt_at")
                .HasFilter("state IN ('queued', 'running')");
        });

        modelBuilder.Entity<AuditRecordRow>(record =>
        {
            record.ToTable("audit_records", table => table.HasCheckConstraint("ck_audit_records_actor_kind", "actor_kind IN ('account', 'webhook', 'job', 'operator')"));
            record.HasKey(r => r.Id).HasName("pk_audit_records");
            record.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            record.Property(r => r.OccurredAt).HasColumnName("occurred_at").HasDefaultValueSql("now()");
            record.Property(r => r.ActorKind).HasColumnName("actor_kind");
            record.Property(r => r.Actor).HasColumnName("actor");
            record.Property(r => r.Action).HasColumnName("action");
            record.Property(r => r.Subject).HasColumnName("subject");
        });
    }
}
