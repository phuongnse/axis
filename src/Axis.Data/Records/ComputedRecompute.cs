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
/// write changed after it was read is skipped, never overwritten.
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
        bool unstampedOnly = false)
    {
        var collections = owner.Fields
            .Where(field => field.Type == FieldType.ChildCollection)
            .Select(field => new Collection(
                field,
                application.FindEntity(field.Target!.Id) ?? throw new InvalidOperationException("The application has no entity for the child collection.")))
            .ToList();

        var table = RecordQueries.Table(owner);
        var idColumn = EntityNaming.Quote(EntityNaming.IdColumn);
        var stamp = EntityNaming.Quote(EntityNaming.ComputedReleaseColumn);
        var unstamped = unstampedOnly ? $" WHERE {stamp} IS NULL" : "";

        // One reader at a time can be open on a connection, so the ids are read before any other command runs.
        async Task<List<Guid>> ReadIdsAsync()
        {
            var found = new List<Guid>();
            await using var list = new NpgsqlCommand($"SELECT {idColumn} FROM {table}{unstamped} ORDER BY {idColumn}", connection, transaction);
            await using var reader = await list.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                found.Add(reader.GetGuid(0));
            }

            return found;
        }

        List<Guid>? ids = null;
        if (unstampedOnly)
        {
            ids = await ReadIdsAsync();
            if (ids.Count == 0)
            {
                return;
            }

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

        ids ??= await ReadIdsAsync();

        foreach (var id in ids)
        {
            await RecomputeRecordAsync(connection, transaction, owner, collections, releaseId, id, cancellationToken);
        }
    }

    private static async Task RecomputeRecordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        EntityModel owner,
        List<Collection> collections,
        Guid releaseId,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (await RecordQueries.FindRowAsync(connection, owner, id, cancellationToken) is not { } record)
        {
            return;
        }

        var failures = new List<Failure>();
        var childUpdates = new List<(EntityModel Child, Guid RowId, List<RecordValue> Values)>();
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

            foreach (var (rowId, stored) in await ReadRowsAsync(connection, transaction, child, id, cancellationToken))
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

                    childUpdates.Add((child, rowId, [.. computedFields.Select(field => results.GetValueOrDefault(field.Name) ?? new RecordValue(field, null))]));
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

        // The owner goes first. Its version guard decides whether the rows read are still the stored ones.
        await using (var update = new NpgsqlCommand { Connection = connection, Transaction = transaction })
        {
            update.Parameters.AddWithValue("id", id);
            update.Parameters.AddWithValue("version", record.Version);
            update.Parameters.AddWithValue("release", releaseId);
            var assignments = ownerValues.Select((value, index) =>
                $"{EntityNaming.Quote(EntityNaming.Column(value.Field.Name))} = {RecordCommands.AddValue(update, index, value)}").ToList();
            assignments.Add($"{EntityNaming.Quote(EntityNaming.ComputedReleaseColumn)} = @release");
            update.CommandText =
                $"UPDATE {RecordQueries.Table(owner)} SET {string.Join(", ", assignments)} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id AND {EntityNaming.Quote(EntityNaming.VersionColumn)} = @version";
            if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                return;
            }
        }

        foreach (var (child, rowId, values) in childUpdates)
        {
            await using var update = new NpgsqlCommand { Connection = connection, Transaction = transaction };
            update.Parameters.AddWithValue("id", rowId);
            var assignments = values.Select((value, index) =>
                $"{EntityNaming.Quote(EntityNaming.Column(value.Field.Name))} = {RecordCommands.AddValue(update, index, value)}");
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
        NpgsqlTransaction transaction,
        EntityModel child,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var columns = RecordQueries.Columns(child);
        var selected = new[] { EntityNaming.Quote(EntityNaming.IdColumn) }.Concat(columns.Select(field =>
            EntityNaming.Quote(EntityNaming.Column(field.Name)) + (field.Type == FieldType.Decimal ? "::text" : "")));
        await using var command = new NpgsqlCommand(
            $"SELECT {string.Join(", ", selected)} FROM {RecordQueries.Table(child)} WHERE {EntityNaming.Quote(EntityNaming.OwnerColumn)} = @owner ORDER BY {EntityNaming.Quote(EntityNaming.PositionColumn)}",
            connection,
            transaction);
        command.Parameters.AddWithValue("owner", ownerId);

        var rows = new List<(Guid, JsonObject)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new JsonObject();
            for (var index = 0; index < columns.Count; index++)
            {
                var ordinal = index + 1;
                row[columns[index].Name] = reader.IsDBNull(ordinal) ? null : RecordQueries.ReadValue(reader, ordinal, columns[index]);
            }

            rows.Add((reader.GetGuid(0), row));
        }

        return rows;
    }

    private sealed record Collection(FieldModel Field, EntityModel Child);

    private sealed record Failure(Guid EntityId, Guid RecordId, string Field, string Error);
}
