using System.Buffers.Binary;
using System.Globalization;
using Axis.Data.Naming;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Records;

/// <summary>
/// Moves the stored sensitive values and lookup hashes of a tenant database to its newest data key and lookup key, for
/// <c>tenant data-key rotate</c>, and counts the ones that still name an older key, for <c>tenant data-key retire</c>. It finds
/// the encrypted columns in the database catalogue, not in a release: every <c>bytea</c> column named <c>f_</c> and a field name
/// in the <c>entities</c> schema, and the <c>_lookup</c> column beside it. Only a sensitive field has such a column, and the
/// catalogue also holds the tables that no active release uses any more. A table is rewritten in a transaction of its own, in
/// batches by id. A row is written back only if it still holds the bytes that were read, so a value that a running host wrote
/// meanwhile stays as the host wrote it, and a second run moves it. A value that does not decrypt throws and rolls the table back.
/// No message holds a value or a key.
/// </summary>
public static class SensitiveKeyRotation
{
    private const int BatchSize = 1000;

    private const string Discovery =
        "SELECT table_name, column_name FROM information_schema.columns "
        + "WHERE table_schema = 'entities' AND data_type = 'bytea' "
        + "AND table_name ~ '^e_[0-9a-f]{32}$' AND column_name ~ '^f_[^_]+(_lookup)?$' ORDER BY 1, 2";

    private sealed record EncryptedField(string Name, string Value, string? Lookup);

    private sealed record EncryptedTable(string Name, Guid EntityId, IReadOnlyList<EncryptedField> Fields);

    private sealed record StoredField(EncryptedField Field, byte[]? Value, byte[]? Lookup);

    /// <summary>
    /// Re-encrypts every value under a data key other than <paramref name="dataNumber"/> and recomputes every lookup hash under a
    /// lookup key other than <paramref name="lookupNumber"/>, the newest of each in <paramref name="values"/>. Returns the number
    /// of values and hashes it wrote.
    /// </summary>
    /// <exception cref="SensitiveValueDecryptionException">A stored value does not decrypt. The tables before it stay rewritten.</exception>
    public static async Task<int> RotateAsync(
        NpgsqlConnection connection,
        SensitiveValues values,
        string tenantId,
        int dataNumber,
        int lookupNumber,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        var updated = 0;
        foreach (var table in await DiscoverAsync(connection, cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            updated += await RotateTableAsync(connection, transaction, values, tenantId, table, dataNumber, lookupNumber, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return updated;
    }

    /// <summary>
    /// Counts the stored values that name a data key other than <paramref name="dataNumber"/> and the lookup hashes that name a
    /// lookup key other than <paramref name="lookupNumber"/>.
    /// </summary>
    public static async Task<long> CountOlderAsync(NpgsqlConnection connection, int dataNumber, int lookupNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        long count = 0;
        foreach (var table in await DiscoverAsync(connection, cancellationToken))
        {
            var counts = table.Fields.SelectMany(field => field.Lookup is null ? [field.Value] : new[] { field.Value, field.Lookup }).ToList();
            var select = string.Join(
                ", ",
                table.Fields.SelectMany(field => field.Lookup is null
                    ? new[] { $"count(*) FILTER (WHERE {Older(field.Value, "@data")})" }
                    : [$"count(*) FILTER (WHERE {Older(field.Value, "@data")})", $"count(*) FILTER (WHERE {Older(field.Lookup, "@lookup")})"]));
            await using var command = new NpgsqlCommand($"SELECT {select} FROM {EntityNaming.QualifiedTable(table.Name)}", connection);
            AddPrefixes(command, dataNumber, lookupNumber);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            for (var index = 0; index < counts.Count; index++)
            {
                count += reader.GetInt64(index);
            }
        }

        return count;
    }

    private static async Task<List<EncryptedTable>> DiscoverAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var columns = new List<(string Table, string Column)>();
        await using (var command = new NpgsqlCommand(Discovery, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        const string suffix = "_lookup";
        return [.. columns.GroupBy(column => column.Table).Select(group =>
        {
            var names = group.Select(column => column.Column).ToHashSet(StringComparer.Ordinal);
            var fields = names
                .Where(name => !name.EndsWith(suffix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .Select(name => new EncryptedField(name[2..], name, names.Contains(name + suffix) ? name + suffix : null))
                .ToList();
            return new EncryptedTable(group.Key, Guid.ParseExact(group.Key[2..], "N"), fields);
        })];
    }

    private static async Task<int> RotateTableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SensitiveValues values,
        string tenantId,
        EncryptedTable table,
        int dataNumber,
        int lookupNumber,
        CancellationToken cancellationToken)
    {
        var updated = 0;
        var last = Guid.Empty;
        while (true)
        {
            var batch = await ReadBatchAsync(connection, transaction, table, last, dataNumber, lookupNumber, cancellationToken);
            if (batch.Count == 0)
            {
                return updated;
            }

            last = batch[^1].Id;
            foreach (var field in table.Fields)
            {
                updated += await RotateFieldAsync(connection, transaction, values, tenantId, table, field, batch, dataNumber, lookupNumber, cancellationToken);
            }
        }
    }

    private static async Task<int> RotateFieldAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SensitiveValues values,
        string tenantId,
        EncryptedTable table,
        EncryptedField field,
        List<(Guid Id, StoredField[] Stored)> batch,
        int dataNumber,
        int lookupNumber,
        CancellationToken cancellationToken)
    {
        var position = table.Fields.ToList().IndexOf(field);
        var ids = new List<Guid>();
        var read = new List<byte[]>();
        var oldHashes = new List<byte[]>();
        var newHashes = new List<byte[]>();
        var valueIds = new List<Guid>();
        var oldValues = new List<byte[]>();
        var newValues = new List<byte[]>();
        foreach (var (id, stored) in batch)
        {
            var value = stored[position].Value;
            if (value is null)
            {
                continue;
            }

            var lookup = stored[position].Lookup;
            var dataIsOld = HasOlderNumber(value, dataNumber);
            var lookupIsOld = lookup is not null && HasOlderNumber(lookup, lookupNumber);
            if (!dataIsOld && !lookupIsOld)
            {
                continue;
            }

            var text = await values.DecryptTextAsync(new SensitiveValueScope(tenantId, table.EntityId, id), field.Name, value, cancellationToken);
            if (lookupIsOld)
            {
                ids.Add(id);
                read.Add(value);
                oldHashes.Add(lookup!);
                newHashes.Add(await values.LookupTextAsync(tenantId, table.EntityId, field.Name, text, cancellationToken));
            }

            if (dataIsOld)
            {
                valueIds.Add(id);
                oldValues.Add(value);
                newValues.Add(await values.EncryptTextAsync(new SensitiveValueScope(tenantId, table.EntityId, id), field.Name, text, cancellationToken));
            }
        }

        var updated = 0;
        var column = EntityNaming.Quote(field.Value);
        var qualified = EntityNaming.QualifiedTable(table.Name);
        var idColumn = EntityNaming.Quote(EntityNaming.IdColumn);

        // The hashes first, while the rows still hold the values that were read. A row that changed since the read matches nothing.
        if (ids.Count > 0)
        {
            var lookup = EntityNaming.Quote(field.Lookup!);
            await using var update = new NpgsqlCommand(
                $"UPDATE {qualified} AS t SET {lookup} = v.new_hash FROM unnest(@ids, @read, @old, @new) AS v(id, read, old_hash, new_hash) "
                + $"WHERE t.{idColumn} = v.id AND t.{column} = v.read AND t.{lookup} = v.old_hash",
                connection,
                transaction);
            update.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids.ToArray() });
            update.Parameters.Add(new NpgsqlParameter("read", NpgsqlDbType.Array | NpgsqlDbType.Bytea) { Value = read.ToArray() });
            update.Parameters.Add(new NpgsqlParameter("old", NpgsqlDbType.Array | NpgsqlDbType.Bytea) { Value = oldHashes.ToArray() });
            update.Parameters.Add(new NpgsqlParameter("new", NpgsqlDbType.Array | NpgsqlDbType.Bytea) { Value = newHashes.ToArray() });
            updated += await update.ExecuteNonQueryAsync(cancellationToken);
        }

        if (oldValues.Count > 0)
        {
            await using var update = new NpgsqlCommand(
                $"UPDATE {qualified} AS t SET {column} = v.new_value FROM unnest(@ids, @old, @new) AS v(id, old_value, new_value) "
                + $"WHERE t.{idColumn} = v.id AND t.{column} = v.old_value",
                connection,
                transaction);
            update.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = valueIds.ToArray() });
            update.Parameters.Add(new NpgsqlParameter("old", NpgsqlDbType.Array | NpgsqlDbType.Bytea) { Value = oldValues.ToArray() });
            update.Parameters.Add(new NpgsqlParameter("new", NpgsqlDbType.Array | NpgsqlDbType.Bytea) { Value = newValues.ToArray() });
            updated += await update.ExecuteNonQueryAsync(cancellationToken);
        }

        return updated;
    }

    // The rows after last in id order where any value or hash names another key, with every encrypted column of each.
    private static async Task<List<(Guid Id, StoredField[] Stored)>> ReadBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        EncryptedTable table,
        Guid last,
        int dataNumber,
        int lookupNumber,
        CancellationToken cancellationToken)
    {
        var id = EntityNaming.Quote(EntityNaming.IdColumn);
        var columns = table.Fields.SelectMany(field => field.Lookup is null ? new[] { field.Value } : [field.Value, field.Lookup]).ToList();
        var older = string.Join(
            " OR ",
            table.Fields.SelectMany(field => field.Lookup is null
                ? new[] { Older(field.Value, "@data") }
                : [Older(field.Value, "@data"), Older(field.Lookup, "@lookup")]));
        await using var read = new NpgsqlCommand(
            $"SELECT {id}, {string.Join(", ", columns.Select(EntityNaming.Quote))} FROM {EntityNaming.QualifiedTable(table.Name)} "
            + $"WHERE {id} > @last AND ({older}) ORDER BY {id} LIMIT {BatchSize.ToString(CultureInfo.InvariantCulture)}",
            connection,
            transaction);
        read.Parameters.AddWithValue("last", last);
        AddPrefixes(read, dataNumber, lookupNumber);

        var batch = new List<(Guid, StoredField[])>();
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var ordinal = 1;
            var stored = new StoredField[table.Fields.Count];
            for (var index = 0; index < stored.Length; index++)
            {
                var field = table.Fields[index];
                var value = reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<byte[]>(ordinal);
                ordinal++;
                byte[]? lookup = null;
                if (field.Lookup is not null)
                {
                    lookup = reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<byte[]>(ordinal);
                    ordinal++;
                }

                stored[index] = new StoredField(field, value, lookup);
            }

            batch.Add((reader.GetGuid(0), stored));
        }

        return batch;
    }

    private static string Older(string column, string prefix) =>
        $"({EntityNaming.Quote(column)} IS NOT NULL AND substring({EntityNaming.Quote(column)} from 1 for {SensitiveValues.NumberSize.ToString(CultureInfo.InvariantCulture)}) <> {prefix})";

    private static void AddPrefixes(NpgsqlCommand command, int dataNumber, int lookupNumber)
    {
        command.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Bytea) { Value = Prefix(dataNumber) });
        command.Parameters.Add(new NpgsqlParameter("lookup", NpgsqlDbType.Bytea) { Value = Prefix(lookupNumber) });
    }

    private static byte[] Prefix(int number)
    {
        var prefix = new byte[SensitiveValues.NumberSize];
        BinaryPrimitives.WriteInt32BigEndian(prefix, number);
        return prefix;
    }

    private static bool HasOlderNumber(byte[] stored, int number) =>
        stored.Length < SensitiveValues.NumberSize || !stored.AsSpan(0, SensitiveValues.NumberSize).SequenceEqual(Prefix(number));
}
