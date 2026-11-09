using Axis.ControlPlane.Billing;
using Axis.ControlPlane.SignUp;
using Microsoft.EntityFrameworkCore;

namespace Axis.ControlPlane.Storage;

/// <summary>
/// The <c>control_plane</c> and <c>billing</c> schemas of the control plane database. They hold the durable <c>jobs</c>, the
/// append-only <c>audit_records</c>, the <c>sign_ups</c> and the append-only <c>billing.ledger_entries</c>: the sign-up and billing issues add the rest.
/// The caller supplies the connection to the database.
/// </summary>
public sealed class ControlPlaneDbContext(DbContextOptions<ControlPlaneDbContext> options) : DbContext(options)
{
    public const string Schema = "control_plane";

    public const string BillingSchema = "billing";

    public const string MigrationsHistoryTable = "__control_plane_migrations";

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema));

    public DbSet<JobRow> Jobs => Set<JobRow>();

    public DbSet<AuditRecordRow> AuditRecords => Set<AuditRecordRow>();

    public DbSet<SignUpRow> SignUps => Set<SignUpRow>();

    public DbSet<LedgerEntryRow> LedgerEntries => Set<LedgerEntryRow>();

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

        modelBuilder.Entity<SignUpRow>(signUp =>
        {
            signUp.ToTable("sign_ups", table =>
            {
                table.HasCheckConstraint("ck_sign_ups_state", "state IN ('pending', 'verified', 'expired')");
                table.HasCheckConstraint("ck_sign_ups_language", "language IN ('en', 'vi')");
                table.HasCheckConstraint("ck_sign_ups_token_hash", "token_hash IS NULL OR octet_length(token_hash) = 32");
            });
            signUp.HasKey(s => s.Id).HasName("pk_sign_ups");
            signUp.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            signUp.Property(s => s.FullName).HasColumnName("full_name");
            signUp.Property(s => s.Email).HasColumnName("email");
            signUp.Property(s => s.BusinessName).HasColumnName("business_name");
            signUp.Property(s => s.Subdomain).HasColumnName("subdomain");
            signUp.Property(s => s.Language).HasColumnName("language");
            signUp.Property(s => s.Application).HasColumnName("application");
            signUp.Property(s => s.State).HasColumnName("state").HasDefaultValue("pending");
            signUp.Property(s => s.TokenHash).HasColumnName("token_hash").HasColumnType("bytea");
            signUp.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            signUp.Property(s => s.ExpiresAt).HasColumnName("expires_at");
            signUp.HasIndex(s => s.Subdomain, "ux_sign_ups_pending_subdomain").IsUnique().HasFilter("state = 'pending'");
            signUp.HasIndex(s => s.ExpiresAt, "ix_sign_ups_expires_at").HasFilter("state = 'pending'");
        });

        modelBuilder.Entity<LedgerEntryRow>(entry =>
        {
            entry.ToTable("ledger_entries", BillingSchema, table =>
            {
                table.HasCheckConstraint("ck_ledger_entries_kind", "kind IN ('charge', 'credit', 'payment', 'refund', 'proration')");
                table.HasCheckConstraint("ck_ledger_entries_currency", "currency ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint(
                    "ck_ledger_entries_amount_sign",
                    "(kind IN ('charge', 'refund') AND amount_minor > 0) OR (kind IN ('payment', 'credit') AND amount_minor < 0) OR (kind = 'proration' AND amount_minor <> 0)");
            });
            entry.HasKey(e => e.Id).HasName("pk_ledger_entries");
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            entry.Property(e => e.SubscriptionId).HasColumnName("subscription_id");
            entry.Property(e => e.InvoiceId).HasColumnName("invoice_id");
            entry.Property(e => e.Kind).HasColumnName("kind");
            entry.Property(e => e.AmountMinor).HasColumnName("amount_minor").HasColumnType("bigint");
            entry.Property(e => e.Currency).HasColumnName("currency");
            entry.Property(e => e.ProviderReference).HasColumnName("provider_reference");
            entry.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key");
            entry.Property(e => e.OccurredAt).HasColumnName("occurred_at").HasDefaultValueSql("now()");
            entry.HasIndex(e => e.IdempotencyKey, "ux_ledger_entries_idempotency_key").IsUnique();
            entry.HasIndex(e => e.SubscriptionId, "ix_ledger_entries_subscription_id");
        });
    }
}
