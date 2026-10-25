using Microsoft.EntityFrameworkCore;

namespace Axis.Tenancy.Platform;

/// <summary>
/// The <c>platform</c> schema of the platform database: the tenant directory, which holds the tenants, their hosts
/// and their deployment bindings, and the data protection keys of the server. The caller supplies the connection to the platform database.
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string Schema = "platform";

    public const string MigrationsHistoryTable = "__platform_migrations";

    public DbSet<TenantRow> Tenants => Set<TenantRow>();

    public DbSet<TenantHostRow> TenantHosts => Set<TenantHostRow>();

    public DbSet<TenantBindingRow> TenantBindings => Set<TenantBindingRow>();

    public DbSet<DataProtectionKeyRow> DataProtectionKeys => Set<DataProtectionKeyRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<TenantRow>(tenant =>
        {
            tenant.ToTable("tenants", table => table.HasCheckConstraint(
                "ck_tenants_status",
                "status IN ('provisioning', 'active', 'suspended')"));
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
            tenant.Property(t => t.ClaimUserId).HasColumnName("claim_user_id");
            tenant.Property(t => t.ClaimDisplayName).HasColumnName("claim_display_name");
            tenant.Property(t => t.ClaimRoles).HasColumnName("claim_roles");
            tenant.Property(t => t.EndpointAudience).HasColumnName("endpoint_audience");
            tenant.Property(t => t.ServiceAudience).HasColumnName("service_audience");
            tenant.Property(t => t.SchemaVersion).HasColumnName("schema_version").HasDefaultValue("");
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
        });

        modelBuilder.Entity<TenantHostRow>(host =>
        {
            host.ToTable("tenant_hosts");
            host.HasKey(h => new { h.TenantId, h.Host }).HasName("pk_tenant_hosts");
            host.Property(h => h.TenantId).HasColumnName("tenant_id");
            host.Property(h => h.Host).HasColumnName("host");
        });

        modelBuilder.Entity<TenantBindingRow>(binding =>
        {
            binding.ToTable("tenant_bindings", table => table.HasCheckConstraint(
                "ck_tenant_bindings_kind",
                "kind IN ('url', 'secret')"));
            binding.HasKey(b => new { b.TenantId, b.Name }).HasName("pk_tenant_bindings");
            binding.Property(b => b.TenantId).HasColumnName("tenant_id");
            binding.Property(b => b.Name).HasColumnName("name");
            binding.Property(b => b.Kind).HasColumnName("kind");
            binding.Property(b => b.Url).HasColumnName("url");
            binding.Property(b => b.SecretValue).HasColumnName("secret_value").HasColumnType("bytea");
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
    }
}
