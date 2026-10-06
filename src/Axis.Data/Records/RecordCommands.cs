using Axis.Configuration.Model;
using Axis.Data.Naming;
using Npgsql;
using NpgsqlTypes;
using Messages = Axis.Data.Records.RecordInputMessages;

namespace Axis.Data.Records;

/// <summary>
/// Creates, updates and deletes records of an entity table over a tenant connection. Every
/// identifier comes from <see cref="EntityNaming"/> and is quoted, and every value is a typed
/// parameter. Storage failures the request can cause become a <see cref="RecordWriteResult"/> or
/// <see cref="RecordDeleteOutcome"/> that never names a table, column or constraint.
/// </summary>
public static class RecordCommands
{
    private const string ValuesPointer = "/values/";

    /// <summary>The alias of the written row, read back with its labels in the same statement.</summary>
    private const string WriteAlias = "w";

    /// <summary>
    /// Inserts a record with a new version 7 id and version 1, holding <paramref name="values"/>
    /// and SQL <c>NULL</c> or the column default for every other field.
    /// </summary>
    public static async Task<RecordWriteResult> CreateAsync(
        NpgsqlConnection connection,
        EntityModel entity,
        IReadOnlyList<RecordValue> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);

        if (await FindMissingReferencesAsync(connection, values, cancellationToken) is { } missing)
        {
            return missing;
        }

        await using var command = new NpgsqlCommand { Connection = connection };
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        var columns = new List<string> { EntityNaming.Quote(EntityNaming.IdColumn), EntityNaming.Quote(EntityNaming.VersionColumn) };
        var placeholders = new List<string> { "@id", "1" };
        for (var index = 0; index < values.Count; index++)
        {
            columns.Add(EntityNaming.Quote(EntityNaming.Column(values[index].Field.Name)));
            placeholders.Add(AddValue(command, index, values[index]));
        }

        command.CommandText = WithLabels(
            entity,
            $"INSERT INTO {RecordQueries.Table(entity)} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)}) RETURNING *");
        return await WriteAsync(command, entity, cancellationToken)
            ?? throw new InvalidOperationException("The insert returned no row.");
    }

    /// <summary>
    /// Sets <paramref name="values"/> on the record with <paramref name="id"/> and increments its
    /// version, when its version is <paramref name="version"/>. Fields not in
    /// <paramref name="values"/> are left untouched; no values only increments the version.
    /// </summary>
    public static async Task<RecordWriteResult> UpdateAsync(
        NpgsqlConnection connection,
        EntityModel entity,
        Guid id,
        long version,
        IReadOnlyList<RecordValue> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);

        if (await FindMissingReferencesAsync(connection, values, cancellationToken) is { } missing)
        {
            return missing;
        }

        var table = RecordQueries.Table(entity);
        var idColumn = EntityNaming.Quote(EntityNaming.IdColumn);
        var versionColumn = EntityNaming.Quote(EntityNaming.VersionColumn);
        await using var command = new NpgsqlCommand { Connection = connection };
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("version", version);
        var assignments = new List<string>();
        for (var index = 0; index < values.Count; index++)
        {
            assignments.Add($"{EntityNaming.Quote(EntityNaming.Column(values[index].Field.Name))} = {AddValue(command, index, values[index])}");
        }

        assignments.Add($"{versionColumn} = {versionColumn} + 1");
        command.CommandText = WithLabels(
            entity,
            $"UPDATE {table} SET {string.Join(", ", assignments)} WHERE {idColumn} = @id AND {versionColumn} = @version RETURNING *");
        if (await WriteAsync(command, entity, cancellationToken) is { } result)
        {
            return result;
        }

        // One UPDATE cannot tell an unknown id from a stale version, so the id decides.
        return await ExistsAsync(connection, table, id, cancellationToken)
            ? new RecordWriteResult(RecordWriteOutcome.StaleVersion)
            : new RecordWriteResult(RecordWriteOutcome.NotFound);
    }

    /// <summary>
    /// Deletes the record with <paramref name="id"/>. A record that another record references is
    /// kept and is <see cref="RecordDeleteOutcome.Referenced"/>.
    /// </summary>
    public static async Task<RecordDeleteOutcome> DeleteAsync(
        NpgsqlConnection connection,
        EntityModel entity,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(entity);

        await using var command = new NpgsqlCommand(
            $"DELETE FROM {RecordQueries.Table(entity)} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id",
            connection);
        command.Parameters.AddWithValue("id", id);
        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken) == 0
                ? RecordDeleteOutcome.NotFound
                : RecordDeleteOutcome.Deleted;
        }
        // The violated constraint belongs to the referencing table, which may be another entity.
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return RecordDeleteOutcome.Referenced;
        }
    }

    /// <summary>
    /// Wraps a write that returns its row in a data-modifying <c>WITH</c>, and selects the row and
    /// its labels from it. <c>RETURNING</c> cannot join, so this keeps the labels in the write's statement.
    /// The joins see the tables as they were before the write, so a record that references itself
    /// gets the label it had before an update that changes its display field.
    /// </summary>
    private static string WithLabels(EntityModel entity, string write)
    {
        var row = EntityNaming.Quote(WriteAlias);
        return $"WITH {row} AS ({write}) SELECT {RecordQueries.SelectList(entity, WriteAlias)} FROM {row}{RecordQueries.LabelJoins(entity, WriteAlias)}";
    }

    /// <summary>Runs the write and reads the returned row; <see langword="null"/> when no row was written.</summary>
    private static async Task<RecordWriteResult?> WriteAsync(NpgsqlCommand command, EntityModel entity, CancellationToken cancellationToken)
    {
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new RecordWriteResult(RecordWriteOutcome.Written, RecordQueries.ReadRecord(reader, entity))
                : null;
        }
        catch (PostgresException exception) when (MapViolation(exception, entity) is { } result)
        {
            return result;
        }
    }

    /// <summary>
    /// Maps a constraint violation to its result by the constraint names the model declares. The
    /// exception's column and constraint names never reach the result.
    /// </summary>
    private static RecordWriteResult? MapViolation(PostgresException exception, EntityModel entity)
    {
        var table = EntityNaming.Table(entity.Id);
        switch (exception.SqlState)
        {
            // The pre-check missed a target removed before the write; the foreign key is the backstop.
            case PostgresErrorCodes.ForeignKeyViolation:
                var reference = entity.Fields.FirstOrDefault(field =>
                    field.Type == FieldType.Reference
                    && string.Equals(exception.ConstraintName, EntityNaming.ForeignKey(table, EntityNaming.Column(field.Name)), StringComparison.Ordinal));
                return reference is null ? null : Failure(RecordWriteOutcome.MissingReference, reference, Messages.MissingReference);
            case PostgresErrorCodes.UniqueViolation:
                var unique = entity.Fields.FirstOrDefault(field =>
                    field.Unique
                    && string.Equals(exception.ConstraintName, EntityNaming.Unique(table, EntityNaming.Column(field.Name)), StringComparison.Ordinal));
                return unique is null
                    ? new RecordWriteResult(RecordWriteOutcome.SchemaConflict)
                    : Failure(RecordWriteOutcome.UniqueViolation, unique, Messages.NotUnique);
            // A NOT NULL column the active model does not require, left by a failed activation.
            case PostgresErrorCodes.NotNullViolation:
                return new RecordWriteResult(RecordWriteOutcome.SchemaConflict);
            default:
                return null;
        }
    }

    /// <summary>Checks every non-null reference value; returns the errors for those that name no record.</summary>
    private static async Task<RecordWriteResult?> FindMissingReferencesAsync(
        NpgsqlConnection connection,
        IReadOnlyList<RecordValue> values,
        CancellationToken cancellationToken)
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is { Field.Type: FieldType.Reference, Value: Guid target }
                && !await ExistsAsync(connection, EntityNaming.QualifiedTable(EntityNaming.Table(value.Field.Target!.Id)), target, cancellationToken))
            {
                errors[ValuesPointer + value.Field.Name] = [Messages.MissingReference];
            }
        }

        return errors.Count > 0 ? new RecordWriteResult(RecordWriteOutcome.MissingReference, Errors: errors) : null;
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection connection, string table, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT EXISTS (SELECT 1 FROM {table} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id)",
            connection);
        command.Parameters.AddWithValue("id", id);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>
    /// Adds the value as parameter <c>p</c> and its index, typed so that a null binds as a typed
    /// <c>NULL</c>, and returns its SQL placeholder. A decimal is text cast to <c>numeric</c>.
    /// </summary>
    private static string AddValue(NpgsqlCommand command, int index, RecordValue value)
    {
        var name = $"p{index}";
        var type = value.Field.Type switch
        {
            FieldType.Text or FieldType.Enum or FieldType.Decimal => NpgsqlDbType.Text,
            FieldType.Integer => NpgsqlDbType.Bigint,
            FieldType.Boolean => NpgsqlDbType.Boolean,
            FieldType.Date => NpgsqlDbType.Date,
            FieldType.DateTime => NpgsqlDbType.TimestampTz,
            FieldType.Reference => NpgsqlDbType.Uuid,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.Field.Type, "Unknown field type."),
        };
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value.Value ?? DBNull.Value });
        return value.Field.Type == FieldType.Decimal ? $"@{name}::numeric" : $"@{name}";
    }

    private static RecordWriteResult Failure(RecordWriteOutcome outcome, FieldModel field, string message) =>
        new(outcome, Errors: new SortedDictionary<string, string[]>(StringComparer.Ordinal) { [ValuesPointer + field.Name] = [message] });
}
