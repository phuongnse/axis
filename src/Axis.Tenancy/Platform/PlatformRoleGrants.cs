using Npgsql;

namespace Axis.Tenancy.Platform;

/// <summary>
/// Limits the platform reader role, the user of <c>ConnectionStrings:Platform</c>, to reading the
/// <c>platform</c> schema: <c>USAGE</c> on the schema and <c>SELECT</c> on its tables, and <c>INSERT</c> on
/// <c>data_protection_keys</c>, where the server stores its keys, and nothing else. The one exception to reading is
/// <c>audit_records</c>: the role has no right on it at all.
/// Any wider right it had there is revoked. The role name comes from configuration, so an EF Core migration
/// cannot hold it. The server runs this step after the platform migrations.
/// </summary>
public static class PlatformRoleGrants
{
    /// <summary>
    /// Applies the grants over <paramref name="connection"/>, which must be the platform schema role, in one
    /// transaction. It can run any number of times. It throws <see cref="InvalidOperationException"/> when
    /// <paramref name="readerRole"/> is the role the connection uses, because revoking rights from that role
    /// would remove the owner's own access.
    /// </summary>
    public static async Task ApplyAsync(NpgsqlConnection connection, string readerRole, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(readerRole);

        var role = Quote(readerRole);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var check = new NpgsqlCommand("SELECT current_user = @role", connection, transaction))
        {
            check.Parameters.AddWithValue("role", readerRole);
            if ((bool)(await check.ExecuteScalarAsync(cancellationToken))!)
            {
                throw new InvalidOperationException(
                    $"The role of ConnectionStrings:Platform, '{readerRole}', is the role of ConnectionStrings:PlatformSchema. They must be different roles.");
            }
        }

        await using (var grants = new NpgsqlCommand(
            $"""
            REVOKE ALL ON SCHEMA platform FROM {role};
            GRANT USAGE ON SCHEMA platform TO {role};
            REVOKE ALL ON ALL TABLES IN SCHEMA platform FROM {role};
            REVOKE ALL ON ALL SEQUENCES IN SCHEMA platform FROM {role};
            GRANT SELECT ON ALL TABLES IN SCHEMA platform TO {role};
            GRANT INSERT ON platform.data_protection_keys TO {role};
            REVOKE ALL ON platform.audit_records FROM {role};
            ALTER DEFAULT PRIVILEGES IN SCHEMA platform REVOKE ALL ON TABLES FROM {role};
            ALTER DEFAULT PRIVILEGES IN SCHEMA platform GRANT SELECT ON TABLES TO {role};
            """,
            connection,
            transaction))
        {
            await grants.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
