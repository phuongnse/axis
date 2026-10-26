using Microsoft.EntityFrameworkCore;

namespace Axis.Data.Storage;

/// <summary>
/// The data module's tables in a tenant database: what has been provisioned in the
/// <c>entities</c> schema, the migrations applied to it, the counters of the sequences, the audit records, the user
/// profiles, the computed fields whose expression failed for a record, the signed-in sessions, and the uploads of
/// record imports. The caller supplies the connection to the tenant database.
/// </summary>
public sealed class DataDbContext(DbContextOptions<DataDbContext> options) : DbContext(options)
{
    public const string Schema = "axis";

    /// <summary>
    /// Each module context in the <c>axis</c> schema keeps its own migrations history table, so
    /// their histories do not collide.
    /// </summary>
    public const string MigrationsHistoryTable = "__data_migrations";

    public DbSet<ProvisionedEntityRow> ProvisionedEntities => Set<ProvisionedEntityRow>();

    public DbSet<ProvisionedEnumValueRow> ProvisionedEnumValues => Set<ProvisionedEnumValueRow>();

    public DbSet<AppliedMigrationRow> AppliedMigrations => Set<AppliedMigrationRow>();

    public DbSet<SequenceCounterRow> SequenceCounters => Set<SequenceCounterRow>();

    public DbSet<AuditRecordRow> AuditRecords => Set<AuditRecordRow>();

    public DbSet<UserProfileRow> UserProfiles => Set<UserProfileRow>();

    public DbSet<ComputedFieldErrorRow> ComputedFieldErrors => Set<ComputedFieldErrorRow>();

    public DbSet<PendingComputedFieldRow> PendingComputedFields => Set<PendingComputedFieldRow>();

    public DbSet<SessionRow> Sessions => Set<SessionRow>();

    public DbSet<RecordImportRow> RecordImports => Set<RecordImportRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ProvisionedEntityRow>(entity =>
        {
            entity.ToTable("provisioned_entities");
            entity.HasKey(e => e.EntityId).HasName("pk_provisioned_entities");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").ValueGeneratedNever();
            entity.Property(e => e.ApplicationId).HasColumnName("application_id");
            entity.Property(e => e.TableName).HasColumnName("table_name").HasMaxLength(63);
            entity.HasIndex(e => e.ApplicationId).HasDatabaseName("ix_provisioned_entities_application_id");
            entity.HasMany<ProvisionedEnumValueRow>()
                .WithOne()
                .HasForeignKey(value => value.EntityId)
                .HasConstraintName("fk_provisioned_enum_values_provisioned_entities_entity_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProvisionedEnumValueRow>(value =>
        {
            value.ToTable("provisioned_enum_values");
            value.HasKey(v => new { v.EntityId, v.FieldName, v.Value }).HasName("pk_provisioned_enum_values");
            value.Property(v => v.EntityId).HasColumnName("entity_id");
            value.Property(v => v.FieldName).HasColumnName("field_name");
            value.Property(v => v.Value).HasColumnName("value");
        });

        modelBuilder.Entity<AppliedMigrationRow>(migration =>
        {
            migration.ToTable("applied_migrations");
            migration.HasKey(m => m.MigrationId).HasName("pk_applied_migrations");
            migration.Property(m => m.MigrationId).HasColumnName("migration_id").ValueGeneratedNever();
            migration.Property(m => m.ApplicationId).HasColumnName("application_id");
            migration.Property(m => m.ReleaseId).HasColumnName("release_id");
            migration.Property(m => m.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
            migration.Property(m => m.AppliedAt).HasColumnName("applied_at");
            migration.Property(m => m.ChangedColumns).HasColumnName("changed_columns").HasColumnType("jsonb");
            migration.HasIndex(m => m.ApplicationId).HasDatabaseName("ix_applied_migrations_application_id");
        });

        modelBuilder.Entity<SequenceCounterRow>(counter =>
        {
            counter.ToTable("sequence_counters");
            counter.HasKey(c => new { c.SequenceId, c.Period }).HasName("pk_sequence_counters");
            counter.Property(c => c.SequenceId).HasColumnName("sequence_id").ValueGeneratedNever();
            counter.Property(c => c.ApplicationId).HasColumnName("application_id").ValueGeneratedNever();
            counter.Property(c => c.Period).HasColumnName("period").ValueGeneratedNever();
            counter.Property(c => c.LastValue).HasColumnName("last_value").ValueGeneratedNever();
        });

        modelBuilder.Entity<ComputedFieldErrorRow>(error =>
        {
            error.ToTable("computed_field_errors");
            error.HasKey(e => new { e.EntityId, e.RecordId, e.Field }).HasName("pk_computed_field_errors");
            error.Property(e => e.EntityId).HasColumnName("entity_id");
            error.Property(e => e.RecordId).HasColumnName("record_id");
            error.Property(e => e.Field).HasColumnName("field");
            error.Property(e => e.ReleaseId).HasColumnName("release_id");
            error.Property(e => e.Error).HasColumnName("error");
            error.Property(e => e.OccurredAt).HasColumnName("occurred_at");
        });

        modelBuilder.Entity<PendingComputedFieldRow>(pending =>
        {
            pending.ToTable("pending_computed_fields");
            pending.HasKey(p => p.EntityId).HasName("pk_pending_computed_fields");
            pending.Property(p => p.EntityId).HasColumnName("entity_id").ValueGeneratedNever();
            pending.Property(p => p.ApplicationId).HasColumnName("application_id");
            pending.Property(p => p.TargetReleaseId).HasColumnName("target_release_id");
            pending.Property(p => p.CurrentReleaseIds).HasColumnName("current_release_ids");
            pending.Property(p => p.Fields).HasColumnName("fields");
            pending.Property(p => p.CreatedAt).HasColumnName("created_at");
        });

        // A trigger added by the migration rejects every update, delete and truncate.
        modelBuilder.Entity<UserProfileRow>(profile =>
        {
            profile.ToTable("user_profiles");
            profile.HasKey(p => p.UserId).HasName("pk_user_profiles");
            profile.Property(p => p.UserId).HasColumnName("user_id").ValueGeneratedNever();
            profile.Property(p => p.DisplayName).HasColumnName("display_name");
            profile.Property(p => p.Email).HasColumnName("email");
            profile.Property(p => p.Locale).HasColumnName("locale");
            profile.Property(p => p.FirstSignedInAt).HasColumnName("first_signed_in_at");
            profile.Property(p => p.LastSignedInAt).HasColumnName("last_signed_in_at");
        });

        modelBuilder.Entity<AuditRecordRow>(audit =>
        {
            audit.ToTable("audit_records");
            audit.HasKey(a => a.Id).HasName("pk_audit_records");
            audit.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            audit.Property(a => a.OccurredAt).HasColumnName("occurred_at");
            audit.Property(a => a.Actor).HasColumnName("actor");
            audit.Property(a => a.ActorName).HasColumnName("actor_name");
            audit.Property(a => a.Action).HasColumnName("action");
            audit.Property(a => a.ApplicationId).HasColumnName("application_id");
            audit.Property(a => a.EntityId).HasColumnName("entity_id");
            audit.Property(a => a.RecordId).HasColumnName("record_id");
            audit.Property(a => a.ProcessInstanceId).HasColumnName("process_instance_id");
            audit.Property(a => a.Details).HasColumnName("details").HasColumnType("jsonb");
            audit.HasIndex(a => new { a.EntityId, a.RecordId, a.OccurredAt, a.Id })
                .IsDescending(false, false, true, true)
                .HasDatabaseName("ix_audit_records_entity_id_record_id_occurred_at_id");
        });

        modelBuilder.Entity<SessionRow>(session =>
        {
            session.ToTable("sessions");
            session.HasKey(s => s.IdHash).HasName("pk_sessions");
            session.Property(s => s.IdHash).HasColumnName("id_hash").HasColumnType("bytea").ValueGeneratedNever();
            session.Property(s => s.UserId).HasColumnName("user_id");
            session.Property(s => s.DisplayName).HasColumnName("display_name");
            session.Property(s => s.Roles).HasColumnName("roles").HasColumnType("text[]");
            session.Property(s => s.SignedInAt).HasColumnName("signed_in_at");
            session.Property(s => s.LastRequestAt).HasColumnName("last_request_at");
        });

        modelBuilder.Entity<RecordImportRow>(import =>
        {
            import.ToTable("record_imports", table => table.HasCheckConstraint("ck_record_imports_state", "state IN ('pending', 'checked', 'failed', 'committed')"));
            import.HasKey(i => i.Id).HasName("pk_record_imports");
            import.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
            import.Property(i => i.TenantId).HasColumnName("tenant_id");
            import.Property(i => i.ApplicationId).HasColumnName("application_id");
            import.Property(i => i.EntityId).HasColumnName("entity_id");
            import.Property(i => i.UploaderId).HasColumnName("uploader_id");
            import.Property(i => i.UploaderName).HasColumnName("uploader_name");
            import.Property(i => i.UploaderRoles).HasColumnName("uploader_roles").HasColumnType("text[]");
            import.Property(i => i.Key).HasColumnName("key");
            import.Property(i => i.State).HasColumnName("state");
            import.Property(i => i.File).HasColumnName("file").HasColumnType("bytea");
            import.Property(i => i.Errors).HasColumnName("errors").HasColumnType("jsonb");
            import.Property(i => i.WorkItemId).HasColumnName("work_item_id");
            import.Property(i => i.CreatedAt).HasColumnName("created_at");
            import.HasIndex(i => i.WorkItemId).IsUnique().HasDatabaseName("ix_record_imports_work_item_id");
        });
    }
}
