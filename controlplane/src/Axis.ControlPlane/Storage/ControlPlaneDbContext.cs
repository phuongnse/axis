using Axis.ControlPlane.Billing;
using Axis.ControlPlane.SignUp;
using Axis.ControlPlane.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Axis.ControlPlane.Storage;

/// <summary>
/// The <c>control_plane</c> and <c>billing</c> schemas of the control plane database. They hold the durable <c>jobs</c>, the
/// append-only <c>audit_records</c>, the <c>sign_ups</c>, the <c>sign_up_challenges</c>, the <c>disposable_domains</c>, the <c>subscriptions</c>, the <c>subscription_emails</c> and the append-only <c>billing.ledger_entries</c>: the billing issues add the rest.
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

    public DbSet<DisposableDomainRow> DisposableDomains => Set<DisposableDomainRow>();

    public DbSet<SignUpChallengeRow> SignUpChallenges => Set<SignUpChallengeRow>();

    public DbSet<SubscriptionRow> Subscriptions => Set<SubscriptionRow>();

    public DbSet<SubscriptionEmailRow> SubscriptionEmails => Set<SubscriptionEmailRow>();

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
            signUp.Property(s => s.TermsVersion).HasColumnName("terms_version");
            signUp.Property(s => s.DpaVersion).HasColumnName("dpa_version");
            signUp.Property(s => s.PrivacyVersion).HasColumnName("privacy_version");
            signUp.Property(s => s.State).HasColumnName("state").HasDefaultValue("pending");
            signUp.Property(s => s.TokenHash).HasColumnName("token_hash").HasColumnType("bytea");
            signUp.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            signUp.Property(s => s.ExpiresAt).HasColumnName("expires_at");
            signUp.HasIndex(s => s.Subdomain, "ux_sign_ups_pending_subdomain").IsUnique().HasFilter("state = 'pending'");
            signUp.HasIndex(s => s.TokenHash, "ux_sign_ups_token_hash").IsUnique().HasFilter("token_hash IS NOT NULL");
            signUp.HasIndex(s => s.ExpiresAt, "ix_sign_ups_expires_at").HasFilter("state = 'pending'");
        });

        modelBuilder.Entity<DisposableDomainRow>(domain =>
        {
            domain.ToTable("disposable_domains", table => table.HasCheckConstraint("ck_disposable_domains_domain", "domain = lower(domain)"));
            domain.HasKey(d => d.Domain).HasName("pk_disposable_domains");
            domain.Property(d => d.Domain).HasColumnName("domain").ValueGeneratedNever();
        });

        modelBuilder.Entity<SignUpChallengeRow>(challenge =>
        {
            challenge.ToTable("sign_up_challenges", table => table.HasCheckConstraint("ck_sign_up_challenges_challenge", "octet_length(challenge) = 32"));
            challenge.HasKey(c => c.Challenge).HasName("pk_sign_up_challenges");
            challenge.Property(c => c.Challenge).HasColumnName("challenge").HasColumnType("bytea").ValueGeneratedNever();
            challenge.Property(c => c.ExpiresAt).HasColumnName("expires_at");
            challenge.HasIndex(c => c.ExpiresAt, "ix_sign_up_challenges_expires_at");
        });

        modelBuilder.Entity<SubscriptionRow>(subscription =>
        {
            subscription.ToTable("subscriptions", table => table.HasCheckConstraint("ck_subscriptions_state", "state IN ('trialing', 'readOnly', 'deleting', 'deleted')"));
            subscription.HasKey(s => s.Id).HasName("pk_subscriptions");
            subscription.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            subscription.Property(s => s.TenantId).HasColumnName("tenant_id");
            subscription.Property(s => s.SignUpId).HasColumnName("sign_up_id");
            subscription.Property(s => s.State).HasColumnName("state");
            subscription.Property(s => s.TrialEndsAt).HasColumnName("trial_ends_at");
            subscription.Property(s => s.NextAt).HasColumnName("next_at");
            subscription.Property(s => s.StepId).HasColumnName("step_id").ValueGeneratedNever();
            subscription.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            subscription.HasOne<SignUpRow>().WithMany().HasForeignKey(s => s.SignUpId).HasConstraintName("fk_subscriptions_sign_up_id").OnDelete(DeleteBehavior.Restrict);
            subscription.HasIndex(s => s.SignUpId, "ix_subscriptions_sign_up_id");
            subscription.HasIndex(s => s.TenantId, "ux_subscriptions_tenant_id").IsUnique().HasFilter("state <> 'deleted'");
            subscription.HasIndex(s => s.NextAt, "ix_subscriptions_next_at").HasFilter("state <> 'deleted'");
        });

        modelBuilder.Entity<SubscriptionEmailRow>(email =>
        {
            email.ToTable("subscription_emails");
            email.HasKey(e => new { e.SubscriptionId, e.Kind }).HasName("pk_subscription_emails");
            email.Property(e => e.SubscriptionId).HasColumnName("subscription_id");
            email.Property(e => e.Kind).HasColumnName("kind");
            email.Property(e => e.QueuedAt).HasColumnName("queued_at").HasDefaultValueSql("now()");
            email.HasOne<SubscriptionRow>().WithMany().HasForeignKey(e => e.SubscriptionId).HasConstraintName("fk_subscription_emails_subscription_id").OnDelete(DeleteBehavior.Restrict);
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
