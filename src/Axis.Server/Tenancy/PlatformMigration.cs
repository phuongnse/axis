using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Server.Tenancy;

/// <summary>
/// What <see cref="PlatformMigration"/> needs: the connection string of the platform schema role and the
/// name of the platform reader role, the user of <c>ConnectionStrings:Platform</c>.
/// </summary>
internal sealed record PlatformMigrationOptions(string SchemaConnectionString, string ReaderRole);

/// <summary>
/// Under <c>Tenancy:Source=directory</c>, applies the <c>platform</c> schema migrations over the
/// <c>ConnectionStrings:PlatformSchema</c> role, then limits the reader role to reading that schema. It runs in
/// <see cref="StartingAsync"/>, before startup activation, so the server is not listening yet. A failure stops
/// the start.
/// </summary>
internal sealed partial class PlatformMigration(
    PlatformMigrationOptions options,
    ILogger<PlatformMigration> logger) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var contextOptions = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(options.SchemaConnectionString)
            .Options;
        await using (var context = new PlatformDbContext(contextOptions))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        await using var connection = new NpgsqlConnection(options.SchemaConnectionString);
        await connection.OpenAsync(cancellationToken);
        await PlatformRoleGrants.ApplyAsync(connection, options.ReaderRole, cancellationToken);

        LogMigrated(options.ReaderRole);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(LogLevel.Information, "Applied the platform schema migrations and limited the role {ReaderRole} to reading the platform schema.")]
    private partial void LogMigrated(string readerRole);
}
