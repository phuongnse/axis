using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Expressions.Evaluation;
using Npgsql;
using Messages = Axis.Data.Records.RecordInputMessages;

namespace Axis.Data.Records;

/// <summary>
/// Recomputes the stored computed values of every record of one entity, and of the rows of its child
/// collections, with a release, inside the transaction of an activation. The owner's expressions read the
/// rows' new values, as a record write does. Each record is stamped with the release. The recompute leaves
/// <c>version</c> unchanged, writes no audit record and starts no trigger. A record that a concurrent
/// write changed after it was read is skipped, never overwritten. A recompute with a <see cref="RecordCipher"/>
/// reads the sensitive values decrypted and seals the sensitive computed values it writes. Without one, a record
/// that holds a stored sensitive value fails the recompute and nothing is written.
/// <para>
/// A computed field whose expression fails for a record, or reads a value that cannot be held exactly, gets
/// <c>NULL</c>, and <c>axis.computed_field_errors</c> gets a row with the fixed message of the record API.
/// The rows of the entity and its child entities from earlier recomputes are deleted first, because every
/// record is computed again.
/// </para>
/// <para>
/// A recompute can be limited to the records whose stamp is <c>NULL</c>, the rows that a migration <c>fill</c>
/// or <c>convert</c> changed, or whose child rows it changed. It then deletes only the error rows of those
/// records and of their child rows.
/// </para>
/// </summary>
internal static class ComputedRecompute
{
    /// <summary>
    /// Recomputes every record of <paramref name="owner"/>, an entity that is not a child entity, with
    /// <paramref name="releaseId"/>, on <paramref name="connection"/> inside <paramref name="transaction"/>. With
    /// <paramref name="unstampedOnly"/> it recomputes only the records whose stamp is <c>NULL</c>.
    /// </summary>
    public static async Task RecomputeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApplicationModel application,
        EntityModel owner,
        Guid releaseId,
        CancellationToken cancellationToken,
        bool unstampedOnly = false,
        RecordCipher? cipher = null)
    {
        var collections = CollectionsOf(application, owner);

        var table = RecordQueries.Table(owner);
        var idColumn = EntityNaming.Quote(EntityNaming.IdColumn);
        var stamp = EntityNaming.Quote(EntityNaming.ComputedReleaseColumn);
        var unstamped = unstampedOnly ? $" WHERE {stamp} IS NULL" : "";

        List<Guid>? ids = null;
        if (unstampedOnly)
        {
            ids = await ReadIdsAsync(connection, transaction, $"SELECT {idColumn} FROM {table}{unstamped} ORDER BY {idColumn}", null, cancellationToken);
            if (ids.Count == 0)
            {
                return;
            }

            await DeleteErrorsOfAsync(connection, transaction, owner, collections, ids, cancellationToken);
        }
        else
        {
            await using var delete = new NpgsqlCommand("DELETE FROM axis.computed_field_errors WHERE entity_id = ANY(@ids)", connection, transaction);
            delete.Parameters.AddWithValue("ids", new[] { owner.Id }.Concat(collections.Select(collection => collection.Child.Id)).ToArray());
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!owner.HasComputedFields && collections.All(collection => !collection.Child.HasComputedFields))
        {
            await using var stampOnly = new NpgsqlCommand($"UPDATE {table} SET {stamp} = @release{unstamped}", connection, transaction);
            stampOnly.Parameters.AddWithValue("release", releaseId);
            await stampOnly.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        ids ??= await ReadIdsAsync(connection, transaction, $"SELECT {idColumn} FROM {table} ORDER BY {idColumn}", null, cancellationToken);

        foreach (var id in ids)
        {
            await RecomputeRecordAsync(connection, transaction, owner, collections, releaseId, id, cipher, cancellationToken);
        }
    }

    /// <summary>
    /// Recomputes the records of <paramref name="owner"/> named by <paramref name="ids"/> with
    /// <paramref name="releaseId"/>, in the order given. It first deletes the error rows of those records and
    /// of their child rows. A record that no longer exists, or that a concurrent write changed after it was
    /// read, is skipped.
    /// </summary>
    public static async Task RecomputeIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApplicationModel application,
        EntityModel owner,
        Guid releaseId,
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken,
        RecordCipher? cipher = null)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var collections = CollectionsOf(application, owner);
        await DeleteErrorsOfAsync(connection, transaction, owner, collections, ids, cancellationToken);
        foreach (var id in ids)
        {
            await RecomputeRecordAsync(connection, transaction, owner, collections, releaseId, id, cipher, cancellationToken);
        }
    }

    internal static List<Collection> CollectionsOf(ApplicationModel application, EntityModel owner) =>
        owner.Fields
            .Where(field => field.Type == FieldType.ChildCollection)
            .Select(field => new Collection(
                field,
                application.FindEntity(field.Target!.Id) ?? throw new InvalidOperationException("The application has no entity for the child collection.")))
            .ToList();

    // One reader at a time can be open on a connection, so the ids are read before any other command runs.
    internal static async Task<List<Guid>> ReadIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        Action<NpgsqlParameterCollection>? parameters,
        CancellationToken cancellationToken)
    {
        var found = new List<Guid>();
        await using var list = new NpgsqlCommand(sql, connection, transaction);
        parameters?.Invoke(list.Parameters);
        await using var reader = await list.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            found.Add(reader.GetGuid(0));
        }

        return found;
    }

    private static async Task DeleteErrorsOfAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        EntityModel owner,
        List<Collection> collections,
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var idColumn = EntityNaming.Quote(EntityNaming.IdColumn);
        await using (var delete = new NpgsqlCommand("DELETE FROM axis.computed_field_errors WHERE entity_id = @entity AND record_id = ANY(@ids)", connection, transaction))
        {
            delete.Parameters.AddWithValue("entity", owner.Id);
            delete.Parameters.AddWithValue("ids", ids.ToArray());
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var collection in collections)
        {
            await using var delete = new NpgsqlCommand(
                $"DELETE FROM axis.computed_field_errors WHERE entity_id = @entity AND record_id IN (SELECT {idColumn} FROM {RecordQueries.Table(collection.Child)} WHERE {EntityNaming.Quote(EntityNaming.OwnerColumn)} = ANY(@ids))",
                connection,
                transaction);
            delete.Parameters.AddWithValue("entity", collection.Child.Id);
            delete.Parameters.AddWithValue("ids", ids.ToArray());
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Computes the computed fields of the record with <paramref name="id"/> and of its child rows from the
    /// stored values, with the expressions of <paramref name="owner"/> and the child entities of
    /// <paramref name="collections"/>, and writes nothing. Returns <see langword="null"/> when the record does not
    /// exist. The child values are in collection order and then position order, for the child entities that have
    /// computed fields. A field that failed has <c>NULL</c> and a <see cref="Failure"/>.
    /// </summary>
    internal static async Task<Computed?> ComputeRecordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        EntityModel owner,
        List<Collection> collections,
        Guid id,
        RecordCipher? cipher,
        CancellationToken cancellationToken)
    {
        if (await RecordQueries.FindRowAsync(connection, owner, id, cipher, cancellationToken) is not { } record)
        {
            return null;
        }

        var failures = new List<Failure>();
        var childValues = new List<ChildValues>();
        var allExact = true;
        var items = new Dictionary<string, List<ExpressionValues>>(StringComparer.Ordinal);
        foreach (var (collection, child) in collections)
        {
            var computedFields = child.Fields.Where(field => field.IsComputed).ToList();
            var rows = new List<ExpressionValues>();
            items[collection.Name] = rows;
            if (!owner.HasComputedFields && computedFields.Count == 0)
            {
                continue;
            }

            foreach (var (rowId, stored) in await ReadRowsAsync(connection, transaction, child, id, cipher, cancellationToken))
            {
                var inputs = RecordComputer.NewRecord(child);
                foreach (var column in RecordQueries.Columns(child).Where(column => !column.IsComputed))
                {
                    var exact = RecordClrValues.TryFromStored(column, stored[column.Name], out var value);
                    inputs[column.Name] = (value, exact);
                }

                var results = new Dictionary<string, RecordValue>(StringComparer.Ordinal);
                if (computedFields.Count > 0)
                {
                    if (inputs.Values.All(input => input.Exact))
                    {
                        results = RecordComputer.EvaluateComputed(child, inputs, (field, message) => failures.Add(new Failure(child.Id, rowId, field.Name, message)));
                    }
                    else
                    {
                        failures.AddRange(computedFields.Select(field => new Failure(child.Id, rowId, field.Name, Messages.NotEvaluable)));
                    }

                    childValues.Add(new ChildValues(collection.Name, child, rowId, [.. computedFields.Select(field => results.GetValueOrDefault(field.Name) ?? new RecordValue(field, null))]));
                }

                // The owner's aggregates read every column of the row: the new computed values and the stored rest.
                var row = new List<KeyValuePair<string, object?>>();
                foreach (var column in RecordQueries.Columns(child))
                {
                    var (value, exact) = column.IsComputed
                        ? FromComputed(column, results)
                        : inputs[column.Name];
                    allExact &= exact;
                    row.Add(KeyValuePair.Create(column.Name, value));
                }

                rows.Add(new ExpressionValues(row));
            }
        }

        var ownerValues = new List<RecordValue>();
        if (owner.HasComputedFields)
        {
            var inputs = RecordComputer.NewRecord(owner);
            foreach (var column in RecordQueries.Columns(owner).Where(column => !column.IsComputed))
            {
                var exact = RecordClrValues.TryFromStored(column, record.Values.GetValueOrDefault(column.Name), out var value);
                allExact &= exact;
                inputs[column.Name] = (value, exact);
            }

            foreach (var (name, rows) in items)
            {
                inputs[name] = (rows, true);
            }

            var computedFields = owner.Fields.Where(field => field.IsComputed).ToList();
            var results = new Dictionary<string, RecordValue>(StringComparer.Ordinal);
            if (allExact)
            {
                results = RecordComputer.EvaluateComputed(owner, inputs, (field, message) => failures.Add(new Failure(owner.Id, id, field.Name, message)));
            }
            else
            {
                failures.AddRange(computedFields.Select(field => new Failure(owner.Id, id, field.Name, Messages.NotEvaluable)));
            }

            ownerValues.AddRange(computedFields.Select(field => results.GetValueOrDefault(field.Name) ?? new RecordValue(field, null)));
        }

        return new Computed(record, ownerValues, childValues, failures);
    }

    private static async Task RecomputeRecordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        EntityModel owner,
        List<Collection> collections,
        Guid releaseId,
        Guid id,
        RecordCipher? cipher,
        CancellationToken cancellationToken)
    {
        if (await ComputeRecordAsync(connection, transaction, owner, collections, id, cipher, cancellationToken) is not { } computed)
        {
            return;
        }

        var (record, ownerValues, childUpdates, failures) = computed;

        // The owner goes first. Its version guard decides whether the rows read are still the stored ones.
        await using (var update = new NpgsqlCommand { Connection = connection, Transaction = transaction })
        {
            update.Parameters.AddWithValue("id", id);
            update.Parameters.AddWithValue("version", record.Version);
            update.Parameters.AddWithValue("release", releaseId);
            var sealedOwner = await RecordCipher.SealAsync(cipher, owner, id, ownerValues, cancellationToken);
            var assignments = sealedOwner.Select((entry, index) =>
                $"{EntityNaming.Quote(entry.Column)} = {RecordCommands.AddValue(update, index, entry.Value)}").ToList();
            assignments.Add($"{EntityNaming.Quote(EntityNaming.ComputedReleaseColumn)} = @release");
            update.CommandText =
                $"UPDATE {RecordQueries.Table(owner)} SET {string.Join(", ", assignments)} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id AND {EntityNaming.Quote(EntityNaming.VersionColumn)} = @version";
            if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                return;
            }
        }

        foreach (var (_, child, rowId, values) in childUpdates)
        {
            await using var update = new NpgsqlCommand { Connection = connection, Transaction = transaction };
            update.Parameters.AddWithValue("id", rowId);
            var sealedRow = await RecordCipher.SealAsync(cipher, child, rowId, values, cancellationToken);
            var assignments = sealedRow.Select((entry, index) =>
                $"{EntityNaming.Quote(entry.Column)} = {RecordCommands.AddValue(update, index, entry.Value)}");
            update.CommandText = $"UPDATE {RecordQueries.Table(child)} SET {string.Join(", ", assignments)} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id";
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var failure in failures)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO axis.computed_field_errors (entity_id, record_id, field, release_id, error, occurred_at)
                VALUES (@entity, @record, @field, @release, @error, @occurred)
                ON CONFLICT (entity_id, record_id, field) DO UPDATE
                SET release_id = EXCLUDED.release_id, error = EXCLUDED.error, occurred_at = EXCLUDED.occurred_at
                """,
                connection,
                transaction);
            insert.Parameters.AddWithValue("entity", failure.EntityId);
            insert.Parameters.AddWithValue("record", failure.RecordId);
            insert.Parameters.AddWithValue("field", failure.Field);
            insert.Parameters.AddWithValue("release", releaseId);
            insert.Parameters.AddWithValue("error", failure.Error);
            insert.Parameters.AddWithValue("occurred", DateTimeOffset.UtcNow);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>The value the owner's aggregates read for a computed column of a row, <c>null</c> when it failed.</summary>
    private static (object? Value, bool Exact) FromComputed(FieldModel column, Dictionary<string, RecordValue> results)
    {
        if (!results.TryGetValue(column.Name, out var computed))
        {
            return (null, true);
        }

        var value = RecordClrValues.FromInput(computed, out var exact);
        return (value, exact);
    }

    /// <summary>Reads the id and the stored values of each row of <paramref name="child"/> owned by <paramref name="ownerId"/>, in position order.</summary>
    private static async Task<List<(Guid Id, JsonObject Values)>> ReadRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        EntityModel child,
        Guid ownerId,
        RecordCipher? cipher,
        CancellationToken cancellationToken)
    {
        var columns = RecordQueries.Columns(child);
        var selected = new[] { EntityNaming.Quote(EntityNaming.IdColumn) }.Concat(columns.Select(field =>
            EntityNaming.Quote(EntityNaming.Column(field.Name)) + (!field.Sensitive && FieldTypes.HoldsDecimal(field.Type) ? "::text" : "")));
        await using var command = new NpgsqlCommand(
            $"SELECT {string.Join(", ", selected)} FROM {RecordQueries.Table(child)} WHERE {EntityNaming.Quote(EntityNaming.OwnerColumn)} = @owner ORDER BY {EntityNaming.Quote(EntityNaming.PositionColumn)}",
            connection,
            transaction);
        command.Parameters.AddWithValue("owner", ownerId);

        var rows = new List<(Guid, JsonObject)>();
        var sealedRows = new List<(JsonObject Row, Guid RowId, List<(FieldModel Field, byte[] Stored)> Values)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new JsonObject();
            var storedValues = new List<(FieldModel Field, byte[] Stored)>();
            for (var index = 0; index < columns.Count; index++)
            {
                var ordinal = index + 1;
                if (columns[index].Sensitive && !reader.IsDBNull(ordinal))
                {
                    // The cipher opens the bytes below, so the value stays null until then.
                    storedValues.Add((columns[index], reader.GetFieldValue<byte[]>(ordinal)));
                    row[columns[index].Name] = null;
                    continue;
                }

                row[columns[index].Name] = reader.IsDBNull(ordinal) ? null : RecordQueries.ReadValue(reader, ordinal, columns[index]);
            }

            var rowId = reader.GetGuid(0);
            if (storedValues.Count > 0)
            {
                sealedRows.Add((row, rowId, storedValues));
            }

            rows.Add((rowId, row));
        }

        // The decrypted value of a cell is bound to the id of its row.
        foreach (var (row, rowId, storedValues) in sealedRows)
        {
            foreach (var (field, stored) in storedValues)
            {
                row[field.Name] = await RecordCipher.DecryptAsync(cipher, child, rowId, field, stored, cancellationToken);
            }
        }

        return rows;
    }

    internal sealed record Collection(FieldModel Field, EntityModel Child);

    internal sealed record Failure(Guid EntityId, Guid RecordId, string Field, string Error);

    /// <summary>The computed values of the child row <paramref name="RowId"/> of the collection <paramref name="Collection"/>.</summary>
    internal sealed record ChildValues(string Collection, EntityModel Child, Guid RowId, List<RecordValue> Values);

    /// <summary>The stored record, the owner's computed values, those of its child rows and the failures, as <see cref="ComputeRecordAsync"/> works them out.</summary>
    internal sealed record Computed(Record Stored, List<RecordValue> Owner, List<ChildValues> Children, List<Failure> Failures);
}
