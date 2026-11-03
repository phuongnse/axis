using System.Text.RegularExpressions;
using Axis.Data.Naming;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// Removes a restore-check scratch tenant at once, with no grace window: its directory row, which cascades to its hosts, bindings, keys,
/// backup key, backup time, quotas, usage and leases, then its database, its schema and runtime roles, its realm in the identity provider and
/// its folder under <c>Studio:ApplicationsPath</c>. Every part is found from the id alone, never from the directory row, so a part already gone
/// counts as done and a rerun after a stop finishes the job. Platform audit records that name the tenant stay. It refuses any id that does not
/// start with <see cref="IdPrefix"/> before it touches anything, so a real tenant is never removed this way. The admin role is not a superuser,
/// so it grants itself the tenant's roles before it drops the database. An error never carries a host or a connection string.
/// </summary>
internal sealed partial class ScratchTenantRemoval(
    NpgsqlConnectionStringBuilder admin,
    string platformConnectionString,
    TenantRealm realm,
    string? studioApplicationsPath)
{
    public const string IdPrefix = "restore-check-";

    private const int DropAttempts = 5;

    /// <summary>Whether the id starts with <see cref="IdPrefix"/> and is a tenant id: 1 to 50 lowercase letters, digits and hyphens.</summary>
    public static bool IsScratchId(string id) =>
        id.StartsWith(IdPrefix, StringComparison.Ordinal) && IdPattern().IsMatch(id);

    public async Task RemoveAsync(string id, CancellationToken cancellationToken)
    {
        if (!IsScratchId(id))
        {
            throw new InvalidOperationException($"only a scratch tenant, whose id starts with {IdPrefix}, can be removed");
        }

        // The row goes first: no host resolves to the tenant and its keys are destroyed at once. The other parts are found again from the id.
        await using (var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnectionString).Options))
        {
            await platform.Tenants.Where(tenant => tenant.Id == id).ExecuteDeleteAsync(cancellationToken);
        }

        await RemoveDatabaseAndRolesAsync(id, cancellationToken);
        await realm.DeleteAsync(id, cancellationToken);
        RemoveStudioFolder(id);
    }

    private async Task RemoveDatabaseAndRolesAsync(string id, CancellationToken cancellationToken)
    {
        var database = $"axis_{id}";
        var schemaRole = $"axis_{id}_schema";
        var runtimeRole = $"axis_{id}_runtime";

        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // The admin role is not a superuser, and tenant create gave it only SET on the schema role. Dropping the database needs the owner's
        // privileges, and WITH (FORCE) needs the runtime role's to end its sessions.
        foreach (var role in new[] { schemaRole, runtimeRole })
        {
            if (await RoleExistsAsync(connection, role, cancellationToken))
            {
                await ExecuteAsync(connection, $"GRANT {EntityNaming.Quote(role)} TO CURRENT_USER WITH INHERIT TRUE, SET TRUE", cancellationToken);
            }
        }

        await DropDatabaseAsync(connection, database, cancellationToken);
        await ExecuteAsync(connection, $"DROP ROLE IF EXISTS {EntityNaming.Quote(runtimeRole)}", cancellationToken);
        await ExecuteAsync(connection, $"DROP ROLE IF EXISTS {EntityNaming.Quote(schemaRole)}", cancellationToken);
    }

    // A role that is not a superuser cannot use WITH (FORCE) while a superuser's process, such as an autovacuum worker, is in the database.
    // A plain drop cancels autovacuum and waits for sessions that are closing, so it is tried between the forced tries.
    private static async Task DropDatabaseAsync(NpgsqlConnection connection, string database, CancellationToken cancellationToken)
    {
        var name = EntityNaming.Quote(database);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {name} WITH (FORCE)", cancellationToken);
                return;
            }
            catch (PostgresException)
            {
                try
                {
                    await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {name}", cancellationToken);
                    return;
                }
                catch (PostgresException) when (attempt < DropAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
                }
            }
        }
    }

    private void RemoveStudioFolder(string id)
    {
        if (studioApplicationsPath is null)
        {
            return;
        }

        var root = Path.GetFullPath(studioApplicationsPath);
        var folder = Path.GetFullPath(Path.Combine(root, id));
        if (!folder.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("the Studio folder of the tenant is not under the Studio applications folder");
        }

        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task<bool> RoleExistsAsync(NpgsqlConnection connection, string role, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @name)", connection);
        command.Parameters.AddWithValue("name", role);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    // DROP DATABASE cannot run in a transaction block, so each statement is its own command.
    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    [GeneratedRegex(@"^[a-z0-9-]{1,50}\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
