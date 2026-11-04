using Microsoft.EntityFrameworkCore;

namespace Axis.Tenancy.Platform;

/// <summary>
/// The <c>platform</c> schema of the platform database: the tenant directory, which holds the tenants, their hosts,
/// their deployment bindings, their platform sign-in providers, their data keys and lookup keys, the backup keys and last successful backup times of a cell's tenants and their quota overrides, their legal links and those of their sites, their usage and daily counts, the sessions of the cell console's operators, the provisioning jobs, the data protection keys
/// of the server and the append-only platform audit records.
/// The caller supplies the connection to the platform database.
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string Schema = "platform";

    public const string MigrationsHistoryTable = "__platform_migrations";

    public DbSet<TenantRow> Tenants => Set<TenantRow>();

    public DbSet<TenantHostRow> TenantHosts => Set<TenantHostRow>();

    public DbSet<TenantBindingRow> TenantBindings => Set<TenantBindingRow>();

    public DbSet<TenantPlatformProviderRow> TenantPlatformProviders => Set<TenantPlatformProviderRow>();

    public DbSet<TenantDataKeyRow> TenantDataKeys => Set<TenantDataKeyRow>();

    public DbSet<TenantBackupKeyRow> TenantBackupKeys => Set<TenantBackupKeyRow>();

    public DbSet<TenantBackupRow> TenantBackups => Set<TenantBackupRow>();

    public DbSet<TenantQuotaRow> TenantQuotas => Set<TenantQuotaRow>();

    public DbSet<TenantSiteLegalRow> TenantSiteLegalLinks => Set<TenantSiteLegalRow>();

    public DbSet<OperatorSessionRow> OperatorSessions => Set<OperatorSessionRow>();

    public DbSet<TenantUsageLatestRow> TenantUsageLatest => Set<TenantUsageLatestRow>();

    public DbSet<TenantUsageRow> TenantUsage => Set<TenantUsageRow>();

    public DbSet<TenantLeaseRow> TenantLeases => Set<TenantLeaseRow>();

    public DbSet<TenantDailyCounterRow> TenantDailyCounters => Set<TenantDailyCounterRow>();

    public DbSet<DataProtectionKeyRow> DataProtectionKeys => Set<DataProtectionKeyRow>();

    public DbSet<JobRow> Jobs => Set<JobRow>();

    public DbSet<PlatformAuditRecordRow> AuditRecords => Set<PlatformAuditRecordRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<TenantRow>(tenant =>
        {
            tenant.ToTable("tenants", table =>
            {
                table.HasCheckConstraint("ck_tenants_status", "status IN ('provisioning', 'active', 'suspended')");
                table.HasCheckConstraint("ck_tenants_oidc_profile", "oidc_profile IN ('standard', 'fapi2')");
                table.HasCheckConstraint("ck_tenants_oidc_client_key", "(oidc_client_key IS NULL) = (oidc_client_key_id IS NULL)");
                table.HasCheckConstraint("ck_tenants_oidc_previous_client_key", "(oidc_previous_client_key IS NULL) = (oidc_previous_client_key_id IS NULL)");
                table.HasCheckConstraint("ck_tenants_oidc_previous_needs_current", "oidc_previous_client_key IS NULL OR oidc_client_key IS NOT NULL");
                table.HasCheckConstraint("ck_tenants_smtp_from", "(smtp_host IS NULL) = (email_from IS NULL)");
                table.HasCheckConstraint("ck_tenants_terms_version", "(terms_url IS NULL) = (terms_version IS NULL)");
                table.HasCheckConstraint(
                    "ck_tenants_smtp_host",
                    "smtp_host IS NOT NULL OR (smtp_port IS NULL AND smtp_use_tls IS NULL AND smtp_user_name IS NULL AND smtp_password IS NULL)");
            });
            tenant.HasKey(t => t.Id).HasName("pk_tenants");
            tenant.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
            tenant.Property(t => t.Status).HasColumnName("status");
            tenant.Property(t => t.Version).HasColumnName("version").HasDefaultValue(1L);
            tenant.Property(t => t.DatabaseHost).HasColumnName("database_host");
            tenant.Property(t => t.DatabasePort).HasColumnName("database_port");
            tenant.Property(t => t.DatabaseName).HasColumnName("database_name");
            tenant.Property(t => t.RuntimeUser).HasColumnName("runtime_user");
            tenant.Property(t => t.SchemaUser).HasColumnName("schema_user");
            tenant.Property(t => t.RuntimePassword).HasColumnName("runtime_password").HasColumnType("bytea");
            tenant.Property(t => t.SchemaPassword).HasColumnName("schema_password").HasColumnType("bytea");
            tenant.Property(t => t.OidcAuthority).HasColumnName("oidc_authority");
            tenant.Property(t => t.OidcClientId).HasColumnName("oidc_client_id");
            tenant.Property(t => t.OidcClientSecret).HasColumnName("oidc_client_secret").HasColumnType("bytea");
            tenant.Property(t => t.OidcProfile).HasColumnName("oidc_profile").HasDefaultValue(TenantSignInProfiles.Standard);
            tenant.Property(t => t.OidcClientKeyId).HasColumnName("oidc_client_key_id");
            tenant.Property(t => t.OidcClientKey).HasColumnName("oidc_client_key").HasColumnType("bytea");
            tenant.Property(t => t.OidcPreviousClientKeyId).HasColumnName("oidc_previous_client_key_id");
            tenant.Property(t => t.OidcPreviousClientKey).HasColumnName("oidc_previous_client_key").HasColumnType("bytea");
            tenant.Property(t => t.ClaimUserId).HasColumnName("claim_user_id");
            tenant.Property(t => t.ClaimDisplayName).HasColumnName("claim_display_name");
            tenant.Property(t => t.ClaimRoles).HasColumnName("claim_roles");
            tenant.Property(t => t.ClaimEmail).HasColumnName("claim_email");
            tenant.Property(t => t.ClaimLocale).HasColumnName("claim_locale");
            tenant.Property(t => t.EndpointAudience).HasColumnName("endpoint_audience");
            tenant.Property(t => t.ServiceAudience).HasColumnName("service_audience");
            tenant.Property(t => t.SmtpHost).HasColumnName("smtp_host");
            tenant.Property(t => t.SmtpPort).HasColumnName("smtp_port");
            tenant.Property(t => t.SmtpUseTls).HasColumnName("smtp_use_tls");
            tenant.Property(t => t.SmtpUserName).HasColumnName("smtp_user_name");
            tenant.Property(t => t.SmtpPassword).HasColumnName("smtp_password").HasColumnType("bytea");
            tenant.Property(t => t.RealmActivationClientSecret).HasColumnName("realm_activation_client_secret").HasColumnType("bytea");
            tenant.Property(t => t.RealmUsersClientSecret).HasColumnName("realm_users_client_secret").HasColumnType("bytea");
            tenant.Property(t => t.EmailFrom).HasColumnName("email_from");
            tenant.Property(t => t.PrivacyUrl).HasColumnName("privacy_url");
            tenant.Property(t => t.TermsUrl).HasColumnName("terms_url");
            tenant.Property(t => t.TermsVersion).HasColumnName("terms_version");
            tenant.Property(t => t.SchemaVersion).HasColumnName("schema_version").HasDefaultValue("");
            tenant.Property(t => t.PlatformAccounts).HasColumnName("platform_accounts");
            tenant.Property(t => t.PreviewOf).HasColumnName("preview_of");
            tenant.HasIndex(t => t.PreviewOf).IsUnique().HasDatabaseName("ix_tenants_preview_of");
            tenant.HasOne<TenantRow>()
                .WithMany()
                .HasForeignKey(t => t.PreviewOf)
                .HasConstraintName("fk_tenants_tenants_preview_of")
                .OnDelete(DeleteBehavior.Restrict);
            tenant.HasMany<TenantHostRow>()
                .WithOne()
                .HasForeignKey(host => host.TenantId)
                .HasConstraintName("fk_tenant_hosts_tenants_tenant_id")
                .OnDelete(DeleteBehavior.Cascade);
            tenant.HasMany<TenantBindingRow>()
                .WithOne()
                .HasForeignKey(binding => binding.TenantId)
                .HasConstraintName("fk_tenant_bindings_tenants_tenant_id")
                .OnDelete(DeleteBehavior.Cascade);
            tenant.HasMany<TenantPlatformProviderRow>()
                .WithOne()
                .HasForeignKey(provider => provider.TenantId)
                .HasConstraintName("fk_tenant_platform_providers_tenants_tenant_id")
                .OnDelete(DeleteBehavior.Cascade);
            tenant.HasMany<TenantDataKeyRow>()
                .WithOne()
                .HasForeignKey(key => key.TenantId)
                .HasConstraintName("fk_tenant_data_keys_tenants_tenant_id")
                .OnDelete(DeleteBehavior.Cascade);
            tenant.HasMany<TenantBackupKeyRow>()
                .WithOne()
                .HasForeignKey(key => key.TenantId)
                .HasConstraintName("fk_tenant_backup_keys_tenants_tenant_id")
                .OnDelete(DeleteBehavior.Cascade);
            tenant.HasMany<TenantBackupRow>()
                .WithOne()
                .HasForeignKey(backup => backup.TenantId)
                .HasConstraintName("fk_tenant_backups_tenants_tenant_id")
                .OnDelete(DeleteBehavior.Cascade);
            tenant.HasMany<TenantQuotaRow>()
                .WithOne()
                .HasForeignKey(quota => quota.TenantId)
                .HasConstraintName("fk_tenant_quotas_tenants_tenant_id")
                .OnDelete(DeleteBehavior.Cascade);
            tenant.HasMany<TenantSiteLegalRow>()
                .WithOne()
                .HasForeignKey(legal => legal.TenantId)
                .HasConstraintName("fk_tenant_site_legal_links_tenants_tenant_id")
                .OnDelete(DeleteBehavior.Cascade);
            tenant.HasMany<TenantUsageLatestRow>()
                .WithOne()
                .HasForeignKey(usage => usage.TenantId)
                .HasConstraintName("fk_tenant_usage_latest_tenants_tenant_id")
                .OnDelete(DeleteBehavior.Cascade);
            tenant.HasMany<TenantUsageRow>()
                .WithOne()
                .HasForeignKey(usage => usage.TenantId)
                .HasConstraintName("fk_tenant_usage_tenants_tenant_id")
                .OnDelete(DeleteBehavior.Cascade);
            tenant.HasMany<TenantLeaseRow>()
                .WithOne()
                .HasForeignKey(lease => lease.TenantId)
                .HasConstraintName("fk_tenant_leases_tenants_tenant_id")
                .OnDelete(DeleteBehavior.Cascade);
            tenant.HasMany<TenantDailyCounterRow>()
                .WithOne()
                .HasForeignKey(counter => counter.TenantId)
                .HasConstraintName("fk_tenant_daily_counters_tenants_tenant_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TenantHostRow>(host =>
        {
            host.ToTable("tenant_hosts");
            host.HasKey(h => new { h.TenantId, h.Host }).HasName("pk_tenant_hosts");
            host.Property(h => h.TenantId).HasColumnName("tenant_id");
            host.Property(h => h.Host).HasColumnName("host");
            host.Property(h => h.SitePath).HasColumnName("site_path");
            host.HasIndex(h => new { h.TenantId, h.SitePath }).IsUnique().HasDatabaseName("ix_tenant_hosts_tenant_id_site_path");
        });

        modelBuilder.Entity<TenantBindingRow>(binding =>
        {
            binding.ToTable("tenant_bindings", table => table.HasCheckConstraint(
                "ck_tenant_bindings_kind",
                "kind IN ('url', 'secret', 'storage')"));
            binding.HasKey(b => new { b.TenantId, b.Name }).HasName("pk_tenant_bindings");
            binding.Property(b => b.TenantId).HasColumnName("tenant_id");
            binding.Property(b => b.Name).HasColumnName("name");
            binding.Property(b => b.Kind).HasColumnName("kind");
            binding.Property(b => b.Url).HasColumnName("url");
            binding.Property(b => b.SecretValue).HasColumnName("secret_value").HasColumnType("bytea");
        });

        modelBuilder.Entity<TenantPlatformProviderRow>(provider =>
        {
            provider.ToTable("tenant_platform_providers", table => table.HasCheckConstraint(
                "ck_tenant_platform_providers_type",
                "type IN ('oidc', 'saml')"));
            provider.HasKey(p => new { p.TenantId, p.Name }).HasName("pk_tenant_platform_providers");
            provider.Property(p => p.TenantId).HasColumnName("tenant_id");
            provider.Property(p => p.Name).HasColumnName("name");
            provider.Property(p => p.Type).HasColumnName("type");
            provider.Property(p => p.IssuerBinding).HasColumnName("issuer_binding");
            provider.Property(p => p.ClientIdBinding).HasColumnName("client_id_binding");
            provider.Property(p => p.ClientSecretBinding).HasColumnName("client_secret_binding");
            provider.Property(p => p.ClientKeyBinding).HasColumnName("client_key_binding");
            provider.Property(p => p.MetadataBinding).HasColumnName("metadata_binding");
            provider.Property(p => p.ClaimDisplayName).HasColumnName("claim_display_name");
            provider.Property(p => p.ClaimEmail).HasColumnName("claim_email");
            provider.Property(p => p.ClaimLocale).HasColumnName("claim_locale");
            provider.Property(p => p.RolesClaim).HasColumnName("roles_claim");
            provider.Property(p => p.RoleValues).HasColumnName("role_values").HasColumnType("jsonb");
        });

        modelBuilder.Entity<TenantDataKeyRow>(key =>
        {
            key.ToTable("tenant_data_keys", table =>
            {
                table.HasCheckConstraint("ck_tenant_data_keys_kind", "kind IN ('data', 'lookup')");
                table.HasCheckConstraint("ck_tenant_data_keys_number", "number >= 1");
            });
            key.HasKey(k => new { k.TenantId, k.Kind, k.Number }).HasName("pk_tenant_data_keys");
            key.Property(k => k.TenantId).HasColumnName("tenant_id");
            key.Property(k => k.Kind).HasColumnName("kind");
            key.Property(k => k.Number).HasColumnName("number");
            key.Property(k => k.Key).HasColumnName("key").HasColumnType("bytea");
        });

        modelBuilder.Entity<TenantBackupKeyRow>(key =>
        {
            key.ToTable("tenant_backup_keys");
            key.HasKey(k => k.TenantId).HasName("pk_tenant_backup_keys");
            key.Property(k => k.TenantId).HasColumnName("tenant_id");
            key.Property(k => k.Key).HasColumnName("key").HasColumnType("bytea");
        });

        modelBuilder.Entity<TenantBackupRow>(backup =>
        {
            backup.ToTable("tenant_backups");
            backup.HasKey(b => b.TenantId).HasName("pk_tenant_backups");
            backup.Property(b => b.TenantId).HasColumnName("tenant_id");
            backup.Property(b => b.LastSuccessAt).HasColumnName("last_success_at").HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<TenantQuotaRow>(quota =>
        {
            quota.ToTable("tenant_quotas", table => table.HasCheckConstraint("ck_tenant_quotas_value", "value IS NULL OR value >= 0"));
            quota.HasKey(q => new { q.TenantId, q.Quota }).HasName("pk_tenant_quotas");
            quota.Property(q => q.TenantId).HasColumnName("tenant_id");
            quota.Property(q => q.Quota).HasColumnName("quota");
            quota.Property(q => q.Value).HasColumnName("value").HasColumnType("bigint");
            quota.Property(q => q.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<TenantSiteLegalRow>(legal =>
        {
            legal.ToTable("tenant_site_legal_links", table => table.HasCheckConstraint("ck_tenant_site_legal_links_url", "privacy_url IS NOT NULL OR terms_url IS NOT NULL"));
            legal.HasKey(l => new { l.TenantId, l.SitePath }).HasName("pk_tenant_site_legal_links");
            legal.Property(l => l.TenantId).HasColumnName("tenant_id");
            legal.Property(l => l.SitePath).HasColumnName("site_path");
            legal.Property(l => l.PrivacyUrl).HasColumnName("privacy_url");
            legal.Property(l => l.TermsUrl).HasColumnName("terms_url");
        });

        modelBuilder.Entity<TenantUsageLatestRow>(usage =>
        {
            usage.ToTable("tenant_usage_latest", table =>
            {
                table.HasCheckConstraint("ck_tenant_usage_latest_database_bytes", "database_bytes >= 0");
                table.HasCheckConstraint("ck_tenant_usage_latest_file_bytes", "file_bytes >= 0");
            });
            usage.HasKey(u => u.TenantId).HasName("pk_tenant_usage_latest");
            usage.Property(u => u.TenantId).HasColumnName("tenant_id");
            usage.Property(u => u.DatabaseBytes).HasColumnName("database_bytes").HasColumnType("bigint");
            usage.Property(u => u.FileBytes).HasColumnName("file_bytes").HasColumnType("bigint");
            usage.Property(u => u.SampledAt).HasColumnName("sampled_at").HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<TenantUsageRow>(usage =>
        {
            usage.ToTable("tenant_usage", table =>
            {
                table.HasCheckConstraint("ck_tenant_usage_database_bytes", "database_bytes >= 0");
                table.HasCheckConstraint("ck_tenant_usage_file_bytes", "file_bytes >= 0");
                table.HasCheckConstraint("ck_tenant_usage_users", "users IS NULL OR users >= 0");
                table.HasCheckConstraint("ck_tenant_usage_applications", "applications IS NULL OR applications >= 0");
                table.HasCheckConstraint("ck_tenant_usage_emails_sent", "emails_sent >= 0");
                table.HasCheckConstraint("ck_tenant_usage_connector_calls", "connector_calls >= 0");
                table.HasCheckConstraint("ck_tenant_usage_work_items_run", "work_items_run >= 0");
            });
            usage.HasKey(u => new { u.TenantId, u.Day }).HasName("pk_tenant_usage");
            usage.Property(u => u.TenantId).HasColumnName("tenant_id");
            usage.Property(u => u.Day).HasColumnName("day").HasColumnType("date");
            usage.Property(u => u.DatabaseBytes).HasColumnName("database_bytes").HasColumnType("bigint").HasDefaultValue(0L);
            usage.Property(u => u.FileBytes).HasColumnName("file_bytes").HasColumnType("bigint").HasDefaultValue(0L);
            usage.Property(u => u.Users).HasColumnName("users").HasColumnType("integer");
            usage.Property(u => u.Applications).HasColumnName("applications").HasColumnType("integer");
            usage.Property(u => u.EmailsSent).HasColumnName("emails_sent").HasColumnType("integer").HasDefaultValue(0);
            usage.Property(u => u.ConnectorCalls).HasColumnName("connector_calls").HasColumnType("bigint").HasDefaultValue(0L);
            usage.Property(u => u.WorkItemsRun).HasColumnName("work_items_run").HasColumnType("bigint").HasDefaultValue(0L);
        });

        modelBuilder.Entity<TenantLeaseRow>(lease =>
        {
            lease.ToTable("tenant_leases");
            lease.HasKey(l => l.LeaseId).HasName("pk_tenant_leases");
            lease.Property(l => l.LeaseId).HasColumnName("lease_id");
            lease.Property(l => l.TenantId).HasColumnName("tenant_id");
            lease.Property(l => l.Quota).HasColumnName("quota");
            lease.Property(l => l.Holder).HasColumnName("holder");
            lease.Property(l => l.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
            lease.HasIndex(l => new { l.TenantId, l.Quota, l.ExpiresAt }).HasDatabaseName("ix_tenant_leases_tenant_id_quota_expires_at");
        });

        modelBuilder.Entity<OperatorSessionRow>(session =>
        {
            session.ToTable("operator_sessions");
            session.HasKey(s => s.IdHash).HasName("pk_operator_sessions");
            session.Property(s => s.IdHash).HasColumnName("id_hash").HasColumnType("bytea").ValueGeneratedNever();
            session.Property(s => s.OperatorId).HasColumnName("operator_id");
            session.Property(s => s.DisplayName).HasColumnName("display_name");
            session.Property(s => s.Roles).HasColumnName("roles").HasColumnType("text[]");
            session.Property(s => s.SignedInAt).HasColumnName("signed_in_at").HasColumnType("timestamp with time zone");
            session.Property(s => s.LastRequestAt).HasColumnName("last_request_at").HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<TenantDailyCounterRow>(counter =>
        {
            counter.ToTable("tenant_daily_counters", table => table.HasCheckConstraint("ck_tenant_daily_counters_value", "value >= 0"));
            counter.HasKey(c => new { c.TenantId, c.Day, c.Counter }).HasName("pk_tenant_daily_counters");
            counter.Property(c => c.TenantId).HasColumnName("tenant_id");
            counter.Property(c => c.Day).HasColumnName("day").HasColumnType("date");
            counter.Property(c => c.Counter).HasColumnName("counter");
            counter.Property(c => c.Value).HasColumnName("value").HasColumnType("bigint");
        });

        modelBuilder.Entity<DataProtectionKeyRow>(key =>
        {
            key.ToTable("data_protection_keys");
            key.HasKey(k => k.Id).HasName("pk_data_protection_keys");
            key.Property(k => k.Id).HasColumnName("id").ValueGeneratedNever();
            key.Property(k => k.FriendlyName).HasColumnName("friendly_name");
            key.HasIndex(k => k.FriendlyName).IsUnique().HasDatabaseName("ix_data_protection_keys_friendly_name");
            key.Property(k => k.Xml).HasColumnName("xml").HasColumnType("bytea");
        });

        modelBuilder.Entity<PlatformAuditRecordRow>(record =>
        {
            record.ToTable("audit_records");
            record.HasKey(r => r.Id).HasName("pk_audit_records");
            record.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            record.Property(r => r.OccurredAt).HasColumnName("occurred_at");
            record.Property(r => r.Command).HasColumnName("command");
            record.Property(r => r.TenantId).HasColumnName("tenant_id");
            record.Property(r => r.OsUser).HasColumnName("os_user");
            record.Property(r => r.Machine).HasColumnName("machine");
            record.Property(r => r.Changed).HasColumnName("changed").HasColumnType("text[]");
            record.Property(r => r.Actor).HasColumnName("actor");
        });

        modelBuilder.Entity<JobRow>(job =>
        {
            job.ToTable("jobs", table =>
            {
                table.HasCheckConstraint("ck_jobs_kind", "kind IN ('suspend', 'resume', 'create')");
                table.HasCheckConstraint("ck_jobs_state", "state IN ('queued', 'running', 'succeeded', 'failed')");
            });
            job.HasKey(j => j.Id).HasName("pk_jobs");
            job.Property(j => j.Id).HasColumnName("id").ValueGeneratedNever();
            job.Property(j => j.Kind).HasColumnName("kind");
            job.Property(j => j.TenantId).HasColumnName("tenant_id");
            job.Property(j => j.State).HasColumnName("state");
            job.Property(j => j.Steps).HasColumnName("steps").HasColumnType("jsonb");
            job.Property(j => j.Error).HasColumnName("error");
            job.Property(j => j.Input).HasColumnName("input").HasColumnType("jsonb");
            job.Property(j => j.IdempotencyKey).HasColumnName("idempotency_key");
            job.Property(j => j.RequestHash).HasColumnName("request_hash");
            job.Property(j => j.Actor).HasColumnName("actor");
            job.Property(j => j.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("clock_timestamp()");
            job.Property(j => j.StartedAt).HasColumnName("started_at").HasColumnType("timestamp with time zone");
            job.Property(j => j.FinishedAt).HasColumnName("finished_at").HasColumnType("timestamp with time zone");
            job.Property(j => j.ClaimedBy).HasColumnName("claimed_by");
            job.Property(j => j.LeaseUntil).HasColumnName("lease_until").HasColumnType("timestamp with time zone");
            job.Property(j => j.Claims).HasColumnName("claims").HasDefaultValue(0);
            job.HasIndex(j => new { j.State, j.CreatedAt }).HasDatabaseName("ix_jobs_state_created_at");
            job.HasIndex(j => j.IdempotencyKey).HasDatabaseName("ix_jobs_idempotency_key");
        });
    }
}
