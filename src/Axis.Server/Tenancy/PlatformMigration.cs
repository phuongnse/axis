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
/// Applies the <c>platform</c> schema migrations over the <c>ConnectionStrings:PlatformSchema</c> role, then limits
/// the reader role to reading that schema. Under <c>Tenancy:Source=directory</c> it runs while the server builds its
/// services, before the first directory load, so a fresh install has its tables. Under <c>configuration</c> it runs
/// as the first hosted service. A failure stops the start.
/// </summary>
internal static class PlatformMigration
{
    public static async Task ApplyAsync(PlatformMigrationOptions options, CancellationToken cancellationToken)
    {
        // No pooling: the schema role keeps no idle session on the platform database after the start.
        var schemaConnectionString = new NpgsqlConnectionStringBuilder(options.SchemaConnectionString) { Pooling = false }.ConnectionString;
        var contextOptions = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(schemaConnectionString)
            .Options;
        await using (var context = new PlatformDbContext(contextOptions))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        await using var connection = new NpgsqlConnection(schemaConnectionString);
        await connection.OpenAsync(cancellationToken);
        await PlatformRoleGrants.ApplyAsync(connection, options.ReaderRole, cancellationToken);
    }
}
