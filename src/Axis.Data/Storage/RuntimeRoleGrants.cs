using Axis.Data.Naming;
using Npgsql;

namespace Axis.Data.Storage;

/// <summary>
/// Gives the runtime database role its rights in the <c>axis</c> and <c>entities</c> schemas, as the
/// access table in the storage reference says: row access on every <c>entities</c> table and on the
/// <see cref="RuntimeWrittenTables"/>, <c>SELECT</c> and <c>INSERT</c> on the
/// <see cref="RuntimeInsertedTables"/>, <c>SELECT</c> only on every other <c>axis</c> table, and no right
/// to create, alter or drop anything. The role name comes from configuration, so an EF Core migration
/// cannot hold it. The server runs this step both before and after the tenant migrations. The first run
/// sets the default privileges that apply to the tables the migrations create, and the second run
/// covers the tables they created and repairs a database with older, wider grants.
/// </summary>
public static class RuntimeRoleGrants
{
    /// <summary>The <c>pg_advisory_xact_lock</c> key that serializes the grant steps of concurrent servers.</summary>
    public const long LockKey = 0x4158_4953_4752_4E54; // "AXISGRNT" in ASCII.

    /// <summary>
    /// The <c>axis</c> tables that the runtime database role reads and writes. Any other <c>axis</c> table,
    /// including a future one, is read-only for it until someone adds it here.
    /// </summary>
    public static readonly IReadOnlySet<string> RuntimeWrittenTables = new HashSet<string>(StringComparer.Ordinal)
    {
        "process_instances",
        "process_step_history",
        "process_tasks",
        "process_work_items",
        "connector_calls",
        "package_calls",
        "process_call_attempts",
        "events",
        "process_event_matches",
        "notifications",
        "notification_reads",
        "email_outbox",
        "endpoint_receipts",
        "process_start_receipts",
        "trigger_schedules",
        "trigger_starts",
        "delivery_failures",
        "dead_letters",
        "audit_records",
        "user_profiles",
        "sequence_counters",
        "computed_field_errors",
        "pending_computed_fields",
        "sessions",
        "given_roles",
        "files",
        "file_contents",
        "record_imports",
    };

    /// <summary>
    /// The <c>axis</c> tables that the runtime database role may insert into and read, but never update,
    /// delete or truncate.
    /// </summary>
    public static readonly IReadOnlySet<string> RuntimeInsertedTables = new HashSet<string>(StringComparer.Ordinal)
    {
        "publish_requests",
    };

    /// <summary>
    /// Applies the grants over <paramref name="connection"/>, which must be the schema database role, in
    /// one transaction. It can run any number of times. It throws <see cref="InvalidOperationException"/>
    /// when <paramref name="runtimeRole"/> is the role the connection uses, because revoking the write
    /// rights from that role would stop release storage and activation.
    /// </summary>
    public static async Task ApplyAsync(NpgsqlConnection connection, string runtimeRole, CancellationToken cancellationToken = default)
    {
        var role = EntityNaming.Quote(runtimeRole);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Concurrent servers run these grants at the same time, and their GRANT statements update the same catalog rows.
        await using (var lockCommand = new NpgsqlCommand($"SELECT pg_advisory_xact_lock({LockKey})", connection, transaction))
        {
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var check = new NpgsqlCommand("SELECT current_user = @role", connection, transaction))
        {
            check.Parameters.AddWithValue("role", runtimeRole);
            if ((bool)(await check.ExecuteScalarAsync(cancellationToken))!)
            {
                throw new InvalidOperationException(
                    $"The runtime database role '{runtimeRole}' is the schema database role. They must be different roles.");
            }
        }

        await Execute(
            $"""
            CREATE SCHEMA IF NOT EXISTS axis;
            CREATE SCHEMA IF NOT EXISTS entities;
            GRANT USAGE ON SCHEMA axis, entities TO {role};
            ALTER DEFAULT PRIVILEGES REVOKE ALL ON TABLES FROM {role};
            ALTER DEFAULT PRIVILEGES REVOKE ALL ON SEQUENCES FROM {role};
            ALTER DEFAULT PRIVILEGES REVOKE ALL ON SCHEMAS FROM {role};
            ALTER DEFAULT PRIVILEGES IN SCHEMA entities GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {role};
            ALTER DEFAULT PRIVILEGES IN SCHEMA axis REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON TABLES FROM {role};
            ALTER DEFAULT PRIVILEGES IN SCHEMA axis GRANT SELECT ON TABLES TO {role};
            ALTER DEFAULT PRIVILEGES IN SCHEMA axis, entities GRANT USAGE, SELECT ON SEQUENCES TO {role};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA entities TO {role};
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA axis, entities TO {role};
            GRANT SELECT ON ALL TABLES IN SCHEMA axis TO {role};
            """);

        var tables = new List<string>();
        await using (var list = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname = 'axis'", connection, transaction))
        await using (var reader = await list.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (var table in tables)
        {
            var qualified = $"{EntityNaming.Quote("axis")}.{EntityNaming.Quote(table)}";
            await Execute(
                RuntimeWrittenTables.Contains(table)
                    ? $"GRANT SELECT, INSERT, UPDATE, DELETE ON {qualified} TO {role}"
                    : RuntimeInsertedTables.Contains(table)
                        ? $"GRANT SELECT, INSERT ON {qualified} TO {role}; REVOKE UPDATE, DELETE, TRUNCATE ON {qualified} FROM {role}"
                        : $"REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON {qualified} FROM {role}");
        }

        await transaction.CommitAsync(cancellationToken);

        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
