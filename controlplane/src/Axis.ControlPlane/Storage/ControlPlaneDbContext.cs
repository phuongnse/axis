using Axis.ControlPlane.Billing;
using Axis.ControlPlane.SignUp;
using Axis.ControlPlane.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Axis.ControlPlane.Storage;

/// <summary>
/// The <c>control_plane</c> and <c>billing</c> schemas of the control plane database. They hold the durable <c>jobs</c>, the
/// append-only <c>audit_records</c>, the <c>sign_ups</c> with their progress secret hash and provisioning progress, the <c>sign_up_challenges</c>, the <c>disposable_domains</c>, the <c>subscriptions</c>, the <c>subscription_emails</c>, the <c>subscription_add_ons</c>, the <c>quota_overrides</c>, the append-only <c>billing.ledger_entries</c>, the <c>billing.invoices</c>, the <c>billing.invoice_lines</c>, the <c>billing.payment_links</c> and the append-only <c>terms_acceptances</c>: the billing issues add the rest.
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

    public DbSet<SubscriptionAddOnRow> SubscriptionAddOns => Set<SubscriptionAddOnRow>();

    public DbSet<QuotaOverrideRow> QuotaOverrides => Set<QuotaOverrideRow>();

    public DbSet<LedgerEntryRow> LedgerEntries => Set<LedgerEntryRow>();

    public DbSet<InvoiceRow> Invoices => Set<InvoiceRow>();

    public DbSet<InvoiceLineRow> InvoiceLines => Set<InvoiceLineRow>();

    public DbSet<PaymentLinkRow> PaymentLinks => Set<PaymentLinkRow>();

    public DbSet<TermsAcceptanceRow> TermsAcceptances => Set<TermsAcceptanceRow>();

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
                table.HasCheckConstraint("ck_sign_ups_secret_hash", "secret_hash IS NULL OR octet_length(secret_hash) = 32");
                table.HasCheckConstraint("ck_sign_ups_provisioning", "provisioning IS NULL OR provisioning IN ('queued', 'creating', 'ready', 'delayed')");
            });
            signUp.HasKey(s => s.Id).HasName("pk_sign_ups");
            signUp.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            signUp.Property(s => s.FullName).HasColumnName("full_name");
            signUp.Property(s => s.Email).HasColumnName("email");
            signUp.Property(s => s.NormalisedEmail).HasColumnName("normalised_email");
            signUp.Property(s => s.BusinessName).HasColumnName("business_name");
            signUp.Property(s => s.Subdomain).HasColumnName("subdomain");
            signUp.Property(s => s.Language).HasColumnName("language");
            signUp.Property(s => s.Application).HasColumnName("application");
            signUp.Property(s => s.TermsVersion).HasColumnName("terms_version");
            signUp.Property(s => s.DpaVersion).HasColumnName("dpa_version");
            signUp.Property(s => s.PrivacyVersion).HasColumnName("privacy_version");
            signUp.Property(s => s.State).HasColumnName("state").HasDefaultValue("pending");
            signUp.Property(s => s.TokenHash).HasColumnName("token_hash").HasColumnType("bytea");
            signUp.Property(s => s.SecretHash).HasColumnName("secret_hash").HasColumnType("bytea");
            signUp.Property(s => s.Provisioning).HasColumnName("provisioning").HasColumnType("text");
            signUp.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            signUp.Property(s => s.ExpiresAt).HasColumnName("expires_at");
            signUp.HasIndex(s => s.Subdomain, "ux_sign_ups_pending_subdomain").IsUnique().HasFilter("state = 'pending'");
            signUp.HasIndex(s => s.NormalisedEmail, "ux_sign_ups_verified_email").IsUnique().HasFilter("state = 'verified'");
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
            subscription.ToTable("subscriptions", table =>
            {
                table.HasCheckConstraint("ck_subscriptions_state", "state IN ('trialing', 'readOnly', 'deleting', 'deleted')");
                table.HasCheckConstraint("ck_subscriptions_plan", "(plan_code IS NULL) = (plan_version IS NULL)");
                table.HasCheckConstraint("ck_subscriptions_purchase", "(currency IS NULL) = (period IS NULL) AND (currency IS NULL OR plan_code IS NOT NULL)");
                table.HasCheckConstraint("ck_subscriptions_currency", "currency IN ('VND', 'USD')");
                table.HasCheckConstraint("ck_subscriptions_period", "period IN ('month', 'year')");
            });
            subscription.HasKey(s => s.Id).HasName("pk_subscriptions");
            subscription.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            subscription.Property(s => s.TenantId).HasColumnName("tenant_id");
            subscription.Property(s => s.SignUpId).HasColumnName("sign_up_id");
            subscription.Property(s => s.State).HasColumnName("state");
            subscription.Property(s => s.TrialEndsAt).HasColumnName("trial_ends_at");
            subscription.Property(s => s.NextAt).HasColumnName("next_at");
            subscription.Property(s => s.StepId).HasColumnName("step_id").ValueGeneratedNever();
            subscription.Property(s => s.PlanCode).HasColumnName("plan_code");
            subscription.Property(s => s.PlanVersion).HasColumnName("plan_version");
            subscription.Property(s => s.Currency).HasColumnName("currency");
            subscription.Property(s => s.Period).HasColumnName("period");
            subscription.Property(s => s.PeriodEndsAt).HasColumnName("period_ends_at");
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

        modelBuilder.Entity<SubscriptionAddOnRow>(addOn =>
        {
            addOn.ToTable("subscription_add_ons", table => table.HasCheckConstraint("ck_subscription_add_ons_quantity", "quantity >= 1"));
            addOn.HasKey(a => new { a.SubscriptionId, a.AddOnCode }).HasName("pk_subscription_add_ons");
            addOn.Property(a => a.SubscriptionId).HasColumnName("subscription_id");
            addOn.Property(a => a.AddOnCode).HasColumnName("add_on_code");
            addOn.Property(a => a.Quantity).HasColumnName("quantity");
            addOn.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            addOn.HasOne<SubscriptionRow>().WithMany().HasForeignKey(a => a.SubscriptionId).HasConstraintName("fk_subscription_add_ons_subscription_id").OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QuotaOverrideRow>(quotaOverride =>
        {
            quotaOverride.ToTable("quota_overrides", table =>
            {
                table.HasCheckConstraint("ck_quota_overrides_value", "value >= 0");
                table.HasCheckConstraint("ck_quota_overrides_reason", "btrim(reason) <> ''");
            });
            quotaOverride.HasKey(o => new { o.SubscriptionId, o.Quota }).HasName("pk_quota_overrides");
            quotaOverride.Property(o => o.SubscriptionId).HasColumnName("subscription_id");
            quotaOverride.Property(o => o.Quota).HasColumnName("quota");
            quotaOverride.Property(o => o.Value).HasColumnName("value").HasColumnType("bigint");
            quotaOverride.Property(o => o.Reason).HasColumnName("reason");
            quotaOverride.Property(o => o.Until).HasColumnName("until");
            quotaOverride.Property(o => o.Operator).HasColumnName("operator");
            quotaOverride.Property(o => o.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            quotaOverride.HasOne<SubscriptionRow>().WithMany().HasForeignKey(o => o.SubscriptionId).HasConstraintName("fk_quota_overrides_subscription_id").OnDelete(DeleteBehavior.Restrict);
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
            entry.HasIndex(e => e.InvoiceId, "ix_ledger_entries_invoice_id");
        });

        modelBuilder.Entity<InvoiceRow>(invoice =>
        {
            invoice.ToTable("invoices", BillingSchema, table =>
            {
                table.HasCheckConstraint("ck_invoices_kind", "kind IN ('period', 'change')");
                table.HasCheckConstraint("ck_invoices_status", "status IN ('open', 'paid', 'void')");
                table.HasCheckConstraint("ck_invoices_currency", "currency IN ('VND', 'USD')");
                table.HasCheckConstraint("ck_invoices_period", "period IN ('month', 'year')");
                table.HasCheckConstraint("ck_invoices_period_start", "(kind = 'period') = (period_start IS NOT NULL)");
            });
            invoice.HasKey(i => i.Id).HasName("pk_invoices");
            invoice.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
            invoice.Property(i => i.SubscriptionId).HasColumnName("subscription_id");
            invoice.Property(i => i.Kind).HasColumnName("kind");
            invoice.Property(i => i.Status).HasColumnName("status").HasDefaultValue("open");
            invoice.Property(i => i.Currency).HasColumnName("currency");
            invoice.Property(i => i.PlanCode).HasColumnName("plan_code");
            invoice.Property(i => i.PlanVersion).HasColumnName("plan_version");
            invoice.Property(i => i.Period).HasColumnName("period");
            invoice.Property(i => i.PeriodStart).HasColumnName("period_start");
            invoice.Property(i => i.DueAt).HasColumnName("due_at");
            invoice.Property(i => i.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            invoice.HasOne<SubscriptionRow>().WithMany().HasForeignKey(i => i.SubscriptionId).HasConstraintName("fk_invoices_subscription_id").OnDelete(DeleteBehavior.Restrict);
            invoice.HasIndex(i => i.SubscriptionId, "ix_invoices_subscription_id");
            invoice.HasIndex(i => new { i.SubscriptionId, i.PeriodStart }, "ux_invoices_period").IsUnique().HasFilter("kind = 'period'");
        });

        modelBuilder.Entity<InvoiceLineRow>(line =>
        {
            line.ToTable("invoice_lines", BillingSchema, table =>
            {
                table.HasCheckConstraint("ck_invoice_lines_kind", "kind IN ('plan', 'addOn')");
                table.HasCheckConstraint("ck_invoice_lines_quantity", "quantity >= 1");
                table.HasCheckConstraint("ck_invoice_lines_line_number", "line_number >= 1");
            });
            line.HasKey(l => new { l.InvoiceId, l.LineNumber }).HasName("pk_invoice_lines");
            line.Property(l => l.InvoiceId).HasColumnName("invoice_id");
            line.Property(l => l.LineNumber).HasColumnName("line_number");
            line.Property(l => l.Kind).HasColumnName("kind");
            line.Property(l => l.Code).HasColumnName("code");
            line.Property(l => l.Quantity).HasColumnName("quantity");
            line.HasOne<InvoiceRow>().WithMany().HasForeignKey(l => l.InvoiceId).HasConstraintName("fk_invoice_lines_invoice_id").OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.HasSequence<long>("payment_order_codes", BillingSchema).StartsAt(1).HasMax(PaymentLinkRow.MaxOrderCode);

        modelBuilder.Entity<PaymentLinkRow>(link =>
        {
            link.ToTable("payment_links", BillingSchema, table =>
            {
                table.HasCheckConstraint("ck_payment_links_order_code", $"order_code BETWEEN 1 AND {PaymentLinkRow.MaxOrderCode}");
                table.HasCheckConstraint(
                    "ck_payment_links_created",
                    "(created_at IS NULL AND payment_link_id IS NULL AND checkout_url IS NULL AND qr_code IS NULL AND bin IS NULL AND account_number IS NULL AND account_name IS NULL AND description IS NULL)"
                    + " OR (created_at IS NOT NULL AND payment_link_id IS NOT NULL AND checkout_url IS NOT NULL AND qr_code IS NOT NULL AND bin IS NOT NULL AND account_number IS NOT NULL AND account_name IS NOT NULL AND description IS NOT NULL)");
            });
            link.HasKey(l => l.InvoiceId).HasName("pk_payment_links");
            link.Property(l => l.InvoiceId).HasColumnName("invoice_id").ValueGeneratedNever();
            link.Property(l => l.OrderCode).HasColumnName("order_code").HasColumnType("bigint").HasDefaultValueSql("nextval('billing.payment_order_codes')");
            link.Property(l => l.PaymentLinkId).HasColumnName("payment_link_id");
            link.Property(l => l.CheckoutUrl).HasColumnName("checkout_url");
            link.Property(l => l.QrCode).HasColumnName("qr_code");
            link.Property(l => l.Bin).HasColumnName("bin");
            link.Property(l => l.AccountNumber).HasColumnName("account_number");
            link.Property(l => l.AccountName).HasColumnName("account_name");
            link.Property(l => l.Description).HasColumnName("description");
            link.Property(l => l.ReservedAt).HasColumnName("reserved_at").HasDefaultValueSql("now()");
            link.Property(l => l.CreatedAt).HasColumnName("created_at");
            link.HasOne<InvoiceRow>().WithOne().HasForeignKey<PaymentLinkRow>(l => l.InvoiceId).HasConstraintName("fk_payment_links_invoice_id").OnDelete(DeleteBehavior.Restrict);
            link.HasIndex(l => l.OrderCode, "ux_payment_links_order_code").IsUnique();
        });

        modelBuilder.Entity<TermsAcceptanceRow>(acceptance =>
        {
            acceptance.ToTable("terms_acceptances", table => table.HasCheckConstraint("ck_terms_acceptances_document", "document IN ('terms', 'dpa')"));
            acceptance.HasKey(a => a.Id).HasName("pk_terms_acceptances");
            acceptance.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            acceptance.Property(a => a.Account).HasColumnName("account");
            acceptance.Property(a => a.TenantId).HasColumnName("tenant_id");
            acceptance.Property(a => a.Document).HasColumnName("document");
            acceptance.Property(a => a.Version).HasColumnName("version");
            acceptance.Property(a => a.PrivacyVersion).HasColumnName("privacy_version");
            acceptance.Property(a => a.AcceptedAt).HasColumnName("accepted_at").HasDefaultValueSql("now()");
            acceptance.Property(a => a.ClientAddress).HasColumnName("client_address").HasColumnType("inet");
            acceptance.Property(a => a.UserAgent).HasColumnName("user_agent");
            acceptance.HasIndex(a => a.Account, "ix_terms_acceptances_account");
        });
    }
}
