using Microsoft.EntityFrameworkCore;

namespace Axis.Data.Storage;

/// <summary>
/// The data module's tables in a tenant database: what has been provisioned in the
/// <c>entities</c> schema, and the counters of the sequences. The caller supplies the connection
/// to the tenant database.
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

    public DbSet<SequenceCounterRow> SequenceCounters => Set<SequenceCounterRow>();

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

        modelBuilder.Entity<SequenceCounterRow>(counter =>
        {
            counter.ToTable("sequence_counters");
            counter.HasKey(c => new { c.SequenceId, c.Period }).HasName("pk_sequence_counters");
            counter.Property(c => c.SequenceId).HasColumnName("sequence_id").ValueGeneratedNever();
            counter.Property(c => c.ApplicationId).HasColumnName("application_id").ValueGeneratedNever();
            counter.Property(c => c.Period).HasColumnName("period").ValueGeneratedNever();
            counter.Property(c => c.LastValue).HasColumnName("last_value").ValueGeneratedNever();
        });
    }
}
