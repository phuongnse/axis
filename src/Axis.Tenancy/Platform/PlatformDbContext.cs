using Microsoft.EntityFrameworkCore;

namespace Axis.Tenancy.Platform;

/// <summary>
/// The <c>platform</c> schema of the platform database: the tenant directory, which holds the tenants, their hosts,
/// their deployment bindings and their platform sign-in providers, the data protection keys of the server and the
/// append-only platform audit records.
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

    public DbSet<DataProtectionKeyRow> DataProtectionKeys => Set<DataProtectionKeyRow>();

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
        });
    }
}
