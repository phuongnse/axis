using Microsoft.EntityFrameworkCore;

namespace Axis.ControlPlane.Storage;

/// <summary>
/// The <c>control_plane</c> schema of the control plane database. It holds no table yet: the sign-up and billing
/// issues add them. The caller supplies the connection to the database.
/// </summary>
public sealed class ControlPlaneDbContext(DbContextOptions<ControlPlaneDbContext> options) : DbContext(options)
{
    public const string Schema = "control_plane";

    public const string MigrationsHistoryTable = "__control_plane_migrations";

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.HasDefaultSchema(Schema);
}
