using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Data.Schema;
using Npgsql;

namespace Axis.Data;

/// <summary>
/// Builds the non-unique indexes of an application that activation left out of a large table (D77, "Building an index").
/// The caller passes an open connection of the schema role with no transaction, because <c>CREATE INDEX CONCURRENTLY</c>
/// cannot run in a transaction block. Reads and writes on the table continue while an index builds.
/// </summary>
public static class EntityIndexBuilder
{
    /// <summary>
    /// Creates each non-unique index of <paramref name="model"/> that its existing table lacks, and drops and creates again
    /// the ones PostgreSQL marks invalid, for example after a cancelled build. Entities whose table does not exist yet and
    /// unique indexes are left to activation. Returns the names of the indexes it built, in the order it built them.
    /// The build holds the session advisory lock on <see cref="EntityProvisioner.SchemaLockKey"/>, so it never runs
    /// at the same time as an activation.
    /// </summary>
    public static async Task<IReadOnlyList<string>> BuildAsync(
        ApplicationModel model,
        NpgsqlConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(connection);

        await ExecuteAsync(connection, "SELECT pg_advisory_lock(@key)", cancellationToken, lockKey: true);
        try
        {
            var catalog = await CatalogReader.ReadAsync(connection, null, cancellationToken);
            var tables = catalog.Tables.ToDictionary(table => table.Name, StringComparer.Ordinal);
            var built = new List<string>();
            foreach (var entity in model.Entities)
            {
                var name = EntityNaming.Table(entity.Id);
                if (!tables.TryGetValue(name, out var table))
                {
                    continue;
                }

                var catalogByName = table.Indexes.ToDictionary(index => index.Name, StringComparer.Ordinal);
                foreach (var index in EntityIndexes.Expected(entity, name, model.IsChildEntity(entity)).Where(index => !index.Unique))
                {
                    if (catalogByName.TryGetValue(index.Name, out var current))
                    {
                        if (current.Valid)
                        {
                            continue;
                        }

                        await ExecuteAsync(connection, EntityIndexes.DropSql(index.Name, concurrently: true), cancellationToken);
                    }

                    await ExecuteAsync(connection, EntityIndexes.CreateSql(name, index, concurrently: true), cancellationToken);
                    built.Add(index.Name);
                }
            }

            return built;
        }
        finally
        {
            await ExecuteAsync(connection, "SELECT pg_advisory_unlock(@key)", CancellationToken.None, lockKey: true);
        }
    }

    // Each statement runs as its own command: CONCURRENTLY cannot run in a transaction block, which a batch would form.
    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken, bool lockKey = false)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        if (lockKey)
        {
            command.Parameters.AddWithValue("key", EntityProvisioner.SchemaLockKey);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
