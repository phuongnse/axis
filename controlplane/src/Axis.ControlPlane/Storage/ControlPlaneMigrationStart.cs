using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.ControlPlane.Storage;

/// <summary>
/// Applies the <c>control_plane</c> schema migrations over the <c>ControlPlane:ConnectionString</c> role. It runs as
/// a hosted service in its starting step, so a database that cannot be reached, or a migration that fails, stops the start.
/// </summary>
internal sealed class ControlPlaneMigrationStart(ControlPlaneOptions options) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        // No pooling: the role keeps no idle session on the database after the start.
        var connectionString = new NpgsqlConnectionStringBuilder(options.ConnectionString) { Pooling = false }.ConnectionString;
        var contextOptions = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var context = new ControlPlaneDbContext(contextOptions);
        await context.Database.MigrateAsync(cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
