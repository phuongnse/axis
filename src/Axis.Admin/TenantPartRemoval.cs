using Axis.Data.Naming;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// The removal of the parts of one tenant that are found from its id alone: its database, its schema and runtime roles, and its folder under
/// <c>Studio:ApplicationsPath</c>. A part that is already gone counts as done. It checks no id prefix, so its callers decide which tenants they remove.
/// The admin role is not a superuser, so it grants itself the tenant's roles before it drops the database. Both <see cref="ScratchTenantRemoval"/>
/// and <see cref="TenantPurge"/> use it.
/// </summary>
internal static class TenantPartRemoval
{
    private const int DropAttempts = 5;

    public static async Task RemoveDatabaseAndRolesAsync(NpgsqlConnectionStringBuilder admin, string id, CancellationToken cancellationToken)
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

    public static void RemoveStudioFolder(string? studioApplicationsPath, string id)
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
}
