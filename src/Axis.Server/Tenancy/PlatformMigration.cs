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
/// <c>ConnectionStrings:PlatformSchema</c> role, then limits the reader role to reading that schema. It runs while
/// the server builds its services, before the first directory load, so a fresh install has its tables. A failure
/// stops the start.
/// </summary>
internal static class PlatformMigration
{
    public static async Task ApplyAsync(PlatformMigrationOptions options, CancellationToken cancellationToken)
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
    }
}
