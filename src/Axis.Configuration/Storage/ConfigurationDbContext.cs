using Microsoft.EntityFrameworkCore;

namespace Axis.Configuration.Storage;

/// <summary>
/// The configuration module's tables in a tenant database. The caller supplies the connection to
/// the tenant database. Releases are immutable: saving fails when a stored release, resource,
/// package pin or computed fingerprint is modified or deleted, or when a resource, pin or
/// fingerprint is added to a release that is not added in the same save.
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

    public DbSet<ReleasePackage> ReleasePackages => Set<ReleasePackage>();

    public DbSet<ReleaseComputedFingerprint> ReleaseComputedFingerprints => Set<ReleaseComputedFingerprint>();

    public DbSet<ActiveReleaseRow> ActiveReleases => Set<ActiveReleaseRow>();

    public DbSet<ActiveSiteRow> ActiveSites => Set<ActiveSiteRow>();

    public DbSet<ReleaseActivationRow> ReleaseActivations => Set<ReleaseActivationRow>();

    public DbSet<PublishRequest> PublishRequests => Set<PublishRequest>();

    public DbSet<ApplicationSettingsRow> ApplicationSettings => Set<ApplicationSettingsRow>();

    public DbSet<ResourceWriteRow> ResourceWrites => Set<ResourceWriteRow>();

    public DbSet<AuthoringRequest> AuthoringRequests => Set<AuthoringRequest>();

    public DbSet<AuthoringRequestStep> AuthoringRequestSteps => Set<AuthoringRequestStep>();

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
            release.HasMany(r => r.Packages)
                .WithOne()
                .HasForeignKey(package => package.ReleaseId)
                .HasConstraintName("fk_release_packages_releases_release_id")
                .OnDelete(DeleteBehavior.Cascade);
            release.HasMany(r => r.ComputedFingerprints)
                .WithOne()
                .HasForeignKey(fingerprint => fingerprint.ReleaseId)
                .HasConstraintName("fk_release_computed_fingerprints_releases_release_id")
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

        modelBuilder.Entity<ReleasePackage>(package =>
        {
            package.ToTable("release_packages");
            package.HasKey(p => new { p.ReleaseId, p.PackageId }).HasName("pk_release_packages");
            package.Property(p => p.ReleaseId).HasColumnName("release_id");
            package.Property(p => p.PackageId).HasColumnName("package_id").HasMaxLength(100);
            package.Property(p => p.Version).HasColumnName("version").HasMaxLength(50);
        });

        modelBuilder.Entity<ReleaseComputedFingerprint>(fingerprint =>
        {
            fingerprint.ToTable("release_computed_fingerprints");
            fingerprint.HasKey(f => new { f.ReleaseId, f.EntityId }).HasName("pk_release_computed_fingerprints");
            fingerprint.Property(f => f.ReleaseId).HasColumnName("release_id");
            fingerprint.Property(f => f.EntityId).HasColumnName("entity_id");
            fingerprint.Property(f => f.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64);
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

        modelBuilder.Entity<ReleaseActivationRow>(activation =>
        {
            activation.ToTable("release_activations");
            activation.HasKey(a => a.Id).HasName("pk_release_activations");
            activation.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            activation.Property(a => a.ReleaseId).HasColumnName("release_id");
            activation.Property(a => a.ApplicationId).HasColumnName("application_id");
            activation.Property(a => a.Number).HasColumnName("number");
            activation.Property(a => a.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
            activation.Property(a => a.ActivatedAt).HasColumnName("activated_at");
            activation.Property(a => a.Actor).HasColumnName("actor");
            activation.Property(a => a.ActorName).HasColumnName("actor_name");
            activation.Property(a => a.Label).HasColumnName("label");
            activation.Property(a => a.Note).HasColumnName("note");
            activation.Property(a => a.RequestId).HasColumnName("request_id");
            activation.HasIndex(a => a.ReleaseId).HasDatabaseName("ix_release_activations_release_id");
            activation.HasIndex(a => new { a.ApplicationId, a.ReleaseId }).HasDatabaseName("ix_release_activations_application_id_release_id");
            activation.HasIndex(a => new { a.ApplicationId, a.ActivatedAt, a.Id }).HasDatabaseName("ix_release_activations_application_id_activated_at");
            activation.HasOne<Release>()
                .WithMany()
                .HasForeignKey(a => a.ReleaseId)
                .HasConstraintName("fk_release_activations_releases_release_id")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PublishRequest>(request =>
        {
            request.ToTable("publish_requests");
            request.HasKey(r => r.Id).HasName("pk_publish_requests");
            request.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            request.Property(r => r.ApplicationId).HasColumnName("application_id");
            request.Property(r => r.Kind).HasColumnName("kind");
            request.Property(r => r.Status).HasColumnName("status");
            request.Property(r => r.Target).HasColumnName("target").HasDefaultValue("tenant");
            request.Property(r => r.Snapshot).HasColumnName("snapshot").HasColumnType("jsonb");
            request.Property(r => r.ReleaseId).HasColumnName("release_id");
            request.Property(r => r.Label).HasColumnName("label");
            request.Property(r => r.Note).HasColumnName("note");
            request.Property(r => r.RequestedBy).HasColumnName("requested_by");
            request.Property(r => r.RequestedByName).HasColumnName("requested_by_name");
            request.Property(r => r.RequestedAt).HasColumnName("requested_at").HasDefaultValueSql("now()");
            request.Property(r => r.ClaimedBy).HasColumnName("claimed_by");
            request.Property(r => r.LeaseUntil).HasColumnName("lease_until");
            request.Property(r => r.Claims).HasColumnName("claims").HasDefaultValue(0);
            request.Property(r => r.StartedAt).HasColumnName("started_at");
            request.Property(r => r.FinishedAt).HasColumnName("finished_at");
            request.Property(r => r.Diagnostics).HasColumnName("diagnostics").HasColumnType("jsonb");
            request.Property(r => r.ScenarioNames).HasColumnName("scenario_names").HasColumnType("jsonb");
            request.Property(r => r.ScenarioResults).HasColumnName("scenario_results").HasColumnType("jsonb");
            request.Property(r => r.DraftId).HasColumnName("draft_id").HasColumnType("uuid");
            request.Property(r => r.Folder).HasColumnName("folder").HasColumnType("text");
            request.Property(r => r.Client).HasColumnName("client").HasColumnType("text");
            // Serves the publisher's claim of the oldest queued request of an application.
            request.HasIndex(r => new { r.ApplicationId, r.Status, r.RequestedAt, r.Id })
                .HasDatabaseName("ix_publish_requests_application_id_status_requested_at_id");
        });

        modelBuilder.Entity<ApplicationSettingsRow>(settings =>
        {
            settings.ToTable("application_settings");
            settings.HasKey(s => s.ApplicationId).HasName("pk_application_settings");
            settings.Property(s => s.ApplicationId).HasColumnName("application_id").ValueGeneratedNever();
            settings.Property(s => s.AutoAccept).HasColumnName("auto_accept").HasDefaultValue(false);
        });

        modelBuilder.Entity<ResourceWriteRow>(write =>
        {
            write.ToTable("resource_writes");
            write.HasKey(w => new { w.ApplicationId, w.File }).HasName("pk_resource_writes");
            write.Property(w => w.ApplicationId).HasColumnName("application_id").ValueGeneratedNever();
            write.Property(w => w.File).HasColumnName("file");
            write.Property(w => w.Version).HasColumnName("version");
            write.Property(w => w.UserId).HasColumnName("user_id");
            write.Property(w => w.UserName).HasColumnName("user_name");
            write.Property(w => w.WrittenAt).HasColumnName("written_at");
        });

        modelBuilder.Entity<AuthoringRequest>(request =>
        {
            request.ToTable("authoring_requests");
            request.HasKey(r => r.Id).HasName("pk_authoring_requests");
            request.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            request.Property(r => r.ApplicationId).HasColumnName("application_id");
            request.Property(r => r.Author).HasColumnName("author");
            request.Property(r => r.Text).HasColumnName("text");
            request.Property(r => r.Binding).HasColumnName("binding");
            request.Property(r => r.State).HasColumnName("state");
            request.Property(r => r.DraftId).HasColumnName("draft_id");
            request.Property(r => r.Plan).HasColumnName("plan");
            request.Property(r => r.Round).HasColumnName("round").HasDefaultValue(0);
            request.Property(r => r.Tokens).HasColumnName("tokens").HasDefaultValue(0L);
            request.Property(r => r.AutoAccept).HasColumnName("auto_accept").HasDefaultValue(false);
            request.Property(r => r.SentAt).HasColumnName("sent_at").HasDefaultValueSql("now()");
            request.Property(r => r.ApprovedAt).HasColumnName("approved_at");
            request.Property(r => r.FinishedAt).HasColumnName("finished_at");
            request.Property(r => r.ClaimedBy).HasColumnName("claimed_by");
            request.Property(r => r.ClaimedUntil).HasColumnName("claimed_until");
            // Serves the list of an application's newest requests.
            request.HasIndex(r => new { r.ApplicationId, r.SentAt, r.Id })
                .HasDatabaseName("ix_authoring_requests_application_id_sent_at_id");
            // Serves the count of the requests sent this month.
            request.HasIndex(r => r.SentAt).HasDatabaseName("ix_authoring_requests_sent_at");
        });

        modelBuilder.Entity<AuthoringRequestStep>(step =>
        {
            step.ToTable("authoring_request_steps");
            step.HasKey(s => s.Id).HasName("pk_authoring_request_steps");
            step.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            step.Property(s => s.RequestId).HasColumnName("request_id");
            step.Property(s => s.Ordinal).HasColumnName("ordinal");
            step.Property(s => s.Kind).HasColumnName("kind");
            step.Property(s => s.InputSummary).HasColumnName("input_summary");
            step.Property(s => s.Outcome).HasColumnName("outcome");
            step.Property(s => s.Tokens).HasColumnName("tokens").HasDefaultValue(0L);
            step.Property(s => s.Messages).HasColumnName("messages").HasColumnType("jsonb");
            step.Property(s => s.StartedAt).HasColumnName("started_at").HasDefaultValueSql("now()");
            step.Property(s => s.FinishedAt).HasColumnName("finished_at");
            step.HasIndex(s => new { s.RequestId, s.Ordinal }).IsUnique()
                .HasDatabaseName("ux_authoring_request_steps_request_id_ordinal");
            step.HasOne<AuthoringRequest>()
                .WithMany()
                .HasForeignKey(s => s.RequestId)
                .HasConstraintName("fk_authoring_request_steps_authoring_requests_request_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void GuardImmutability()
    {
        // Entries() detects changes first, so property changes on tracked entities are seen here.
        var entries = ChangeTracker.Entries()
            .Where(entry => entry.Entity is Release or ReleaseResource or ReleasePackage or ReleaseComputedFingerprint)
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

            if (entry is { Entity: ReleasePackage package, State: EntityState.Added } && !addedReleaseIds.Contains(package.ReleaseId))
            {
                throw new InvalidOperationException(
                    $"A stored release is immutable; the package '{package.PackageId}' can only be added together with its release.");
            }

            if (entry is { Entity: ReleaseComputedFingerprint fingerprint, State: EntityState.Added } && !addedReleaseIds.Contains(fingerprint.ReleaseId))
            {
                throw new InvalidOperationException(
                    $"A stored release is immutable; the computed fingerprint of entity '{fingerprint.EntityId}' can only be added together with its release.");
            }
        }
    }
}
