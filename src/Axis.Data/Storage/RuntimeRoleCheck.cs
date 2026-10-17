using Npgsql;

namespace Axis.Data.Storage;

/// <summary>
/// Checks that the runtime database role, the user of <c>ConnectionString</c>, can only read and write
/// rows. The role must not be a superuser, must not own the <c>axis</c> or <c>entities</c> schema or be a
/// member of the role that owns it, and must not have <c>CREATE</c> on either schema. The server and the
/// worker run it at start. The first failed check stops the start, in that order, with <c>axis</c> before
/// <c>entities</c>. The message names the tenant and the role, never the connection string.
/// </summary>
public static class RuntimeRoleCheck
{
    /// <summary>
    /// Runs the check over <paramref name="connection"/>, which must be the runtime connection, because
    /// the check reads <c>current_user</c>. A schema that does not exist yet fails no check. It throws
    /// <see cref="InvalidOperationException"/> for the first failed check.
    /// </summary>
    public static async Task CheckAsync(NpgsqlConnection connection, string tenantId, CancellationToken cancellationToken = default)
    {
        string? role = null;
        var superuser = false;
        var owned = new List<string>();
        var creatable = new List<string>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT current_user::text, r.rolsuper, n.nspname,
                   pg_has_role(current_user, n.nspowner, 'MEMBER') AS owns,
                   has_schema_privilege(current_user, n.oid, 'CREATE') AS can_create
            FROM pg_catalog.pg_roles r
            LEFT JOIN pg_catalog.pg_namespace n ON n.nspname IN ('axis', 'entities')
            WHERE r.rolname = current_user
            ORDER BY n.nspname
            """,
            connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                role = reader.GetString(0);
                superuser = reader.GetBoolean(1);
                if (reader.IsDBNull(2))
                {
                    continue;
                }

                var schema = reader.GetString(2);
                if (reader.GetBoolean(3))
                {
                    owned.Add(schema);
                }

                if (reader.GetBoolean(4))
                {
                    creatable.Add(schema);
                }
            }
        }

        var subject = $"The runtime database role '{role}' of tenant '{tenantId}'";
        const string Rule = "It must only read and write rows.";
        if (superuser)
        {
            throw new InvalidOperationException($"{subject} is a superuser. {Rule}");
        }

        if (owned.Count > 0)
        {
            throw new InvalidOperationException($"{subject} owns the '{owned[0]}' schema, or is a member of its owner. {Rule}");
        }

        if (creatable.Count > 0)
        {
            throw new InvalidOperationException($"{subject} may create objects in the '{creatable[0]}' schema. {Rule}");
        }
    }
}
