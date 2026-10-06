using Microsoft.EntityFrameworkCore;

namespace Axis.Configuration.Storage;

/// <summary>
/// The configuration module's tables in a tenant database. The caller supplies the connection to
/// the tenant database. Releases are immutable: saving fails when a stored release or resource is
/// modified or deleted, or when a resource is added to a release that is not added in the same save.
/// This guard covers change tracking only; bulk operations and raw SQL bypass it.
/// </summary>
public sealed class ConfigurationDbContext(DbContextOptions<ConfigurationDbContext> options) : DbContext(options)
{
    public const string Schema = "axis";

    /// <summary>
    /// Each module context in the <c>axis</c> schema keeps its own migrations history table, so
    /// their histories do not collide.
    /// </summary>
    public const string MigrationsHistoryTable = "__configuration_migrations";

    public const string ReleaseIdentityIndex = "ix_releases_application_id_content_hash";

    /// <summary>
    /// The unique index on <c>lower(name)</c> of the active releases. Migrations create it with raw
    /// SQL because the model cannot express an index on an expression.
    /// </summary>
    public const string ActiveReleaseNameIndex = "ix_active_releases_lower_name";

    /// <summary>The unique index on the <c>path</c> of the active sites, so a site path is active for at most one application.</summary>
    public const string ActiveSitePathIndex = "ix_active_sites_path";

    public DbSet<Release> Releases => Set<Release>();

    public DbSet<ReleaseResource> ReleaseResources => Set<ReleaseResource>();

    public DbSet<ActiveReleaseRow> ActiveReleases => Set<ActiveReleaseRow>();

    public DbSet<ActiveSiteRow> ActiveSites => Set<ActiveSiteRow>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardImmutability();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardImmutability();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Release>(release =>
        {
            release.ToTable("releases");
            release.HasKey(r => r.Id).HasName("pk_releases");
            release.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            release.Property(r => r.ApplicationId).HasColumnName("application_id");
            release.Property(r => r.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
            release.Property(r => r.CreatedAt).HasColumnName("created_at");
            release.HasIndex(r => new { r.ApplicationId, r.ContentHash }).IsUnique().HasDatabaseName(ReleaseIdentityIndex);
            release.HasMany(r => r.Resources)
                .WithOne()
                .HasForeignKey(resource => resource.ReleaseId)
                .HasConstraintName("fk_release_resources_releases_release_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReleaseResource>(resource =>
        {
            resource.ToTable("release_resources");
            resource.HasKey(r => new { r.ReleaseId, r.Path }).HasName("pk_release_resources");
            resource.Property(r => r.ReleaseId).HasColumnName("release_id");
            resource.Property(r => r.Path).HasColumnName("path");
            resource.Property(r => r.Content).HasColumnName("content").HasColumnType("text");
        });

        modelBuilder.Entity<ActiveReleaseRow>(active =>
        {
            active.ToTable("active_releases");
            active.HasKey(r => r.ApplicationId).HasName("pk_active_releases");
            active.Property(r => r.ApplicationId).HasColumnName("application_id").ValueGeneratedNever();
            active.Property(r => r.Name).HasColumnName("name").HasMaxLength(60);
            active.Property(r => r.ReleaseId).HasColumnName("release_id");
            active.Property(r => r.ActivatedAt).HasColumnName("activated_at");
            active.HasIndex(r => r.ReleaseId).HasDatabaseName("ix_active_releases_release_id");
            active.HasOne<Release>()
                .WithMany()
                .HasForeignKey(r => r.ReleaseId)
                .HasConstraintName("fk_active_releases_releases_release_id")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ActiveSiteRow>(site =>
        {
            site.ToTable("active_sites");
            site.HasKey(s => new { s.ApplicationId, s.Path }).HasName("pk_active_sites");
            site.Property(s => s.ApplicationId).HasColumnName("application_id");
            site.Property(s => s.Path).HasColumnName("path").HasMaxLength(60);
            site.HasIndex(s => s.Path).IsUnique().HasDatabaseName(ActiveSitePathIndex);
            site.HasOne<ActiveReleaseRow>()
                .WithMany()
                .HasForeignKey(s => s.ApplicationId)
                .HasConstraintName("fk_active_sites_active_releases_application_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void GuardImmutability()
    {
        // Entries() detects changes first, so property changes on tracked entities are seen here.
        var entries = ChangeTracker.Entries()
            .Where(entry => entry.Entity is Release or ReleaseResource)
            .ToList();
        var addedReleaseIds = entries
            .Where(entry => entry is { Entity: Release, State: EntityState.Added })
            .Select(entry => ((Release)entry.Entity).Id)
            .ToHashSet();

        foreach (var entry in entries)
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"A stored release is immutable; the {entry.Metadata.ClrType.Name} cannot be {entry.State.ToString().ToLowerInvariant()}.");
            }

            if (entry is { Entity: ReleaseResource resource, State: EntityState.Added } && !addedReleaseIds.Contains(resource.ReleaseId))
            {
                throw new InvalidOperationException(
                    $"A stored release is immutable; the resource '{resource.Path}' can only be added together with its release.");
            }
        }
    }
}
