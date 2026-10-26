using Axis.Data.Naming;
using Npgsql;

namespace Axis.Data.Schema;

/// <summary>Reads the entity tables of the tenant database from its catalog.</summary>
public static class CatalogReader
{
    private const string TablesSql = """
        SELECT c.relname
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = @schema AND c.relkind = 'r'
        """;

    private const string ColumnsSql = """
        SELECT c.relname, a.attname, format_type(a.atttypid, a.atttypmod), a.attnotnull
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_attribute a ON a.attrelid = c.oid
        WHERE n.nspname = @schema AND c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped
        ORDER BY c.relname, a.attnum
        """;

    // Single-column unique and foreign key constraints, with the referenced table of a foreign key.
    private const string ConstraintsSql = """
        SELECT c.relname, a.attname, con.contype = 'u', r.relname
        FROM pg_constraint con
        JOIN pg_class c ON c.oid = con.conrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = con.conkey[1]
        LEFT JOIN pg_class r ON r.oid = con.confrelid
        WHERE n.nspname = @schema AND c.relkind = 'r' AND con.contype IN ('u', 'f') AND array_length(con.conkey, 1) = 1
        """;

    // The indexes whose names start with ix_, which are the ones the planner owns, with their columns in index order
    // and whether PostgreSQL marks them valid.
    // The pattern escapes the underscore, which is a wildcard in LIKE.
    private const string IndexesSql = """
        SELECT t.relname, i.relname, x.indisunique,
          ARRAY(SELECT a.attname FROM unnest(x.indkey::int2[]) WITH ORDINALITY k(attnum, ord)
                JOIN pg_attribute a ON a.attrelid = x.indrelid AND a.attnum = k.attnum ORDER BY k.ord),
          x.indisvalid
        FROM pg_index x
        JOIN pg_class i ON i.oid = x.indexrelid
        JOIN pg_class t ON t.oid = x.indrelid
        JOIN pg_namespace n ON n.oid = t.relnamespace
        WHERE n.nspname = @schema AND t.relkind = 'r' AND i.relname LIKE 'ix\_%'
        """;

    /// <summary>
    /// Reads every table of the <c>entities</c> schema, in name order, on
    /// <paramref name="connection"/> within <paramref name="transaction"/> when one is given.
    /// </summary>
    public static async Task<CatalogSnapshot> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var tableNames = await QueryAsync(connection, transaction, TablesSql, reader => reader.GetString(0), cancellationToken);
        tableNames.Sort(StringComparer.Ordinal);

        var columns = (await QueryAsync(
                connection,
                transaction,
                ColumnsSql,
                reader => (Table: reader.GetString(0), Name: reader.GetString(1), Type: reader.GetString(2), NotNull: reader.GetBoolean(3)),
                cancellationToken))
            .ToLookup(column => column.Table, StringComparer.Ordinal);

        var constraints = await QueryAsync(
            connection,
            transaction,
            ConstraintsSql,
            reader => (Table: reader.GetString(0), Column: reader.GetString(1), IsUnique: reader.GetBoolean(2), ReferencedTable: reader.IsDBNull(3) ? null : reader.GetString(3)),
            cancellationToken);
        var uniqueColumns = constraints.Where(constraint => constraint.IsUnique).Select(constraint => (constraint.Table, constraint.Column)).ToHashSet();
        var referencedTables = constraints
            .Where(constraint => !constraint.IsUnique && constraint.ReferencedTable is not null)
            .DistinctBy(constraint => (constraint.Table, constraint.Column))
            .ToDictionary(constraint => (constraint.Table, constraint.Column), constraint => constraint.ReferencedTable!);

        var indexes = (await QueryAsync(
                connection,
                transaction,
                IndexesSql,
                reader => (Table: reader.GetString(0), Index: new CatalogIndex(reader.GetString(1), reader.GetFieldValue<string[]>(3), reader.GetBoolean(2)) { Valid = reader.GetBoolean(4) }),
                cancellationToken))
            .ToLookup(row => row.Table, row => row.Index, StringComparer.Ordinal);

        var tables = new List<CatalogTable>();
        foreach (var table in tableNames)
        {
            await using var command = new NpgsqlCommand($"SELECT EXISTS (SELECT 1 FROM {EntityNaming.QualifiedTable(table)})", connection, transaction);
            var hasRows = (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
            tables.Add(new CatalogTable(
                table,
                [
                    .. columns[table].Select(column => new CatalogColumn(
                        column.Name,
                        column.Type,
                        column.NotNull,
                        uniqueColumns.Contains((table, column.Name)),
                        referencedTables.GetValueOrDefault((table, column.Name)))),
                ],
                hasRows)
            {
                Indexes = [.. indexes[table].OrderBy(index => index.Name, StringComparer.Ordinal)],
            });
        }

        return new CatalogSnapshot(tables);
    }

    private static async Task<List<T>> QueryAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        Func<NpgsqlDataReader, T> read,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("schema", EntityNaming.Schema);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(read(reader));
        }

        return rows;
    }
}
