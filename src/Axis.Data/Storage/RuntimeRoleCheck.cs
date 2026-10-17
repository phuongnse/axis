using Npgsql;

namespace Axis.Data.Storage;

/// <summary>
/// Checks that the runtime database role, the user of <c>ConnectionString</c>, can only read and write
/// rows. The role must not be a superuser or a member of a superuser role, must not own the <c>axis</c> or
/// <c>entities</c> schema or be a member of the role that owns it, and must not have <c>CREATE</c> on either
/// schema or be a member of a role that has it. Membership counts whatever its <c>INHERIT</c> option, because
/// the role can <c>SET ROLE</c> to the other role. The server and the worker run it at start. The first failed check stops the start, in that order, with <c>axis</c> before
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
        string? superuserVia = null;
        var owned = new List<string>();
        var creatable = new List<(string Schema, string Via)>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT r.rolname::text,
                   (SELECT s.rolname::text FROM pg_catalog.pg_roles s
                    WHERE s.rolsuper AND pg_has_role(r.oid, s.oid, 'MEMBER')
                    ORDER BY s.oid <> r.oid, s.rolname LIMIT 1) AS superuser_via,
                   n.nspname,
                   pg_has_role(r.oid, n.nspowner, 'MEMBER') AS owns,
                   (SELECT m.rolname::text FROM pg_catalog.pg_roles m
                    WHERE pg_has_role(r.oid, m.oid, 'MEMBER')
                      AND has_schema_privilege(m.oid, n.oid, 'CREATE')
                    ORDER BY m.oid <> r.oid, m.rolname LIMIT 1) AS create_via
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
                superuserVia = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (reader.IsDBNull(2))
                {
                    continue;
                }

                var schema = reader.GetString(2);
                if (reader.GetBoolean(3))
                {
                    owned.Add(schema);
                }

                if (!reader.IsDBNull(4))
                {
                    creatable.Add((schema, reader.GetString(4)));
                }
            }
        }

        var subject = $"The runtime database role '{role}' of tenant '{tenantId}'";
        const string Rule = "It must only read and write rows.";
        if (superuserVia is not null)
        {
            throw new InvalidOperationException(superuserVia == role
                ? $"{subject} is a superuser. {Rule}"
                : $"{subject} is a member of the superuser role '{superuserVia}'. {Rule}");
        }

        if (owned.Count > 0)
        {
            throw new InvalidOperationException($"{subject} owns the '{owned[0]}' schema, or is a member of its owner. {Rule}");
        }

        if (creatable.Count > 0)
        {
            var (schema, via) = creatable[0];
            throw new InvalidOperationException(via == role
                ? $"{subject} may create objects in the '{schema}' schema. {Rule}"
                : $"{subject} may create objects in the '{schema}' schema through the role '{via}'. {Rule}");
        }
    }
}
