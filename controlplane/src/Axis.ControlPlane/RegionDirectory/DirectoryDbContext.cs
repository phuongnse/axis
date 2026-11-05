using Microsoft.EntityFrameworkCore;

namespace Axis.ControlPlane.RegionDirectory;

/// <summary>
/// The <c>directory</c> schema of the region directory's own database. It holds ids, hosts and cells, and no personal data:
/// <c>regions</c>, <c>cells</c>, <c>tenants</c>, <c>hosts</c> and <c>held</c>. The caller supplies the connection to the database.
/// </summary>
public sealed class DirectoryDbContext(DbContextOptions<DirectoryDbContext> options) : DbContext(options)
{
    public const string Schema = "directory";

    public const string MigrationsHistoryTable = "__directory_migrations";

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema));

    public DbSet<RegionRow> Regions => Set<RegionRow>();

    public DbSet<CellRow> Cells => Set<CellRow>();

    public DbSet<TenantRow> Tenants => Set<TenantRow>();

    public DbSet<HostRow> Hosts => Set<HostRow>();

    public DbSet<HeldRow> Held => Set<HeldRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<RegionRow>(region =>
        {
            region.ToTable("regions");
            region.HasKey(r => r.Code).HasName("pk_regions");
            region.Property(r => r.Code).HasColumnName("code").ValueGeneratedNever();
            region.Property(r => r.TenantDomain).HasColumnName("tenant_domain");
            region.Property(r => r.ApiUrl).HasColumnName("api_url");
        });

        modelBuilder.Entity<CellRow>(cell =>
        {
            cell.ToTable("cells", table =>
            {
                table.HasCheckConstraint("ck_cells_cap", "cap > 0");
                table.HasCheckConstraint("ck_cells_tenant_count", "tenant_count >= 0");
            });
            cell.HasKey(c => c.Id).HasName("pk_cells");
            cell.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            cell.Property(c => c.RegionCode).HasColumnName("region_code");
            cell.Property(c => c.InternalUrl).HasColumnName("internal_url");
            cell.Property(c => c.Cap).HasColumnName("cap");
            cell.Property(c => c.TenantCount).HasColumnName("tenant_count").HasDefaultValue(0);
            cell.Property(c => c.TakesNewTenants).HasColumnName("takes_new_tenants").HasDefaultValue(true);
            cell.HasOne<RegionRow>().WithMany().HasForeignKey(c => c.RegionCode)
                .HasConstraintName("fk_cells_region_code").OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TenantRow>(tenant =>
        {
            tenant.ToTable("tenants");
            tenant.HasKey(t => t.Id).HasName("pk_tenants");
            tenant.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
            tenant.Property(t => t.RegionCode).HasColumnName("region_code");
            tenant.Property(t => t.CellId).HasColumnName("cell_id");
            tenant.HasOne<RegionRow>().WithMany().HasForeignKey(t => t.RegionCode)
                .HasConstraintName("fk_tenants_region_code").OnDelete(DeleteBehavior.Restrict);
            tenant.HasOne<CellRow>().WithMany().HasForeignKey(t => t.CellId)
                .HasConstraintName("fk_tenants_cell_id").OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HostRow>(host =>
        {
            host.ToTable("hosts");
            host.HasKey(h => h.Host).HasName("pk_hosts");
            host.Property(h => h.Host).HasColumnName("host").ValueGeneratedNever();
            host.Property(h => h.TenantId).HasColumnName("tenant_id");
            host.Property(h => h.CellId).HasColumnName("cell_id");
            host.HasOne<TenantRow>().WithMany().HasForeignKey(h => h.TenantId)
                .HasConstraintName("fk_hosts_tenant_id").OnDelete(DeleteBehavior.Restrict);
            host.HasOne<CellRow>().WithMany().HasForeignKey(h => h.CellId)
                .HasConstraintName("fk_hosts_cell_id").OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HeldRow>(held =>
        {
            held.ToTable("held", table => table.HasCheckConstraint("ck_held_kind", "kind IN ('tenant', 'host')"));
            held.HasKey(h => new { h.Kind, h.Value }).HasName("pk_held");
            held.Property(h => h.Kind).HasColumnName("kind").ValueGeneratedNever();
            held.Property(h => h.Value).HasColumnName("value").ValueGeneratedNever();
            held.Property(h => h.HeldUntil).HasColumnName("held_until");
        });
    }
}
