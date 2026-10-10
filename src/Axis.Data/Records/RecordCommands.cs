using System.Globalization;
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
/// <see cref="RecordDeleteResult"/> that never names a table, column or constraint.
/// </summary>
public static class RecordCommands
{
    private const string ValuesPointer = "/values/";

    /// <summary>The alias of the written row, read back with its labels in the same statement.</summary>
    private const string WriteAlias = "w";

    /// <summary>
    /// Inserts a record with version 1, holding <paramref name="values"/> and SQL <c>NULL</c> or the
    /// column default for every other field, and the rows of each collection in
    /// <paramref name="rows"/>. Its id is <paramref name="id"/>, such as a seed record's fixed id,
    /// or a new version 7 id when <paramref name="id"/> is not given. Each field that names a
    /// sequence gets the sequence's next number, taken after the reference check so a refused
    /// create returns before it touches a counter. The caller must hold a transaction when
    /// <paramref name="rows"/> is not empty or the entity has a sequence field, and roll it back
    /// unless the record is <see cref="RecordWriteOutcome.Written"/>, which hands the number back.
    /// </summary>
    public static async Task<RecordWriteResult> CreateAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows,
        Guid? id = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(rows);

        if (await FindMissingReferencesAsync(connection, values, cancellationToken) is { } missing)
        {
            return missing;
        }

        values = await WithSequenceNumbersAsync(connection, application, entity, values, cancellationToken);
        await using var command = new NpgsqlCommand { Connection = connection };
        command.Parameters.AddWithValue("id", id ?? Guid.CreateVersion7());
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
        var result = await WriteAsync(command, entity, cancellationToken)
            ?? throw new InvalidOperationException("The insert returned no row.");
        return await WriteRowsAsync(connection, application, entity, result, rows, replace: false, cancellationToken);
    }

    /// <summary>
    /// Sets <paramref name="values"/> on the record with <paramref name="id"/>, replaces all the
    /// rows of each collection in <paramref name="rows"/> and increments its version, when its
    /// version is <paramref name="version"/>. Fields and collections not named are left untouched;
    /// no values and no rows only increments the version. The caller must hold a transaction when
    /// <paramref name="rows"/> is not empty, and roll it back unless the record is
    /// <see cref="RecordWriteOutcome.Written"/>.
    /// </summary>
    public static async Task<RecordWriteResult> UpdateAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        Guid id,
        long version,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(rows);

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
            return await WriteRowsAsync(connection, application, entity, result, rows, replace: true, cancellationToken);
        }

        // One UPDATE cannot tell an unknown id from a stale version, so the id decides.
        return await ExistsAsync(connection, table, id, cancellationToken)
            ? new RecordWriteResult(RecordWriteOutcome.StaleVersion)
            : new RecordWriteResult(RecordWriteOutcome.NotFound);
    }

    /// <summary>
    /// Sets fields of the <paramref name="stored"/> record to values an expression gave, as a
    /// record update does, and writes it at the stored version. Each value must fit its field's
    /// column, and a <c>null</c> for a required field is refused. Then the computed fields are
    /// recomputed over the stored record with the values on top, and the validations run. A value
    /// that does not fit, a computed field that cannot be computed and a failed validation are
    /// <see cref="RecordWriteOutcome.Invalid"/>, keyed <c>/values/&lt;field&gt;</c>, and write
    /// nothing. Read <paramref name="stored"/> with its rows, inside the caller's transaction, and
    /// lock it first with <see cref="LockAsync"/> so it is the latest version. Roll the
    /// transaction back unless the record is <see cref="RecordWriteOutcome.Written"/>.
    /// </summary>
    /// <param name="values">Each field to set with its value, as the expression interpreter gives it.</param>
    /// <param name="now">The start time of the caller's transaction, which <c>now()</c> gives in a validation.</param>
    public static async Task<RecordWriteResult> SetAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        Record stored,
        IReadOnlyList<KeyValuePair<FieldModel, object?>> values,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(values);

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var converted = new List<RecordValue>();
        foreach (var (field, value) in values)
        {
            RecordValue? recordValue = null;
            var message = value is null && field.Required
                ? Messages.Required
                : RecordComputer.ToRecordValue(field, value, out recordValue);
            if (message is not null)
            {
                errors[ValuesPointer + field.Name] = [message];
            }
            else
            {
                converted.Add(recordValue!);
            }
        }

        if (errors.Count > 0)
        {
            return new RecordWriteResult(RecordWriteOutcome.Invalid, Errors: errors);
        }

        var computed = RecordComputer.ComputeUpdate(application, entity, stored, converted, []);
        if (computed.Errors is { } computeErrors)
        {
            return new RecordWriteResult(RecordWriteOutcome.Invalid, Errors: computeErrors);
        }

        if (RecordValidator.ValidateUpdate(application, entity, stored, computed.Values, computed.Rows, now) is { } failures)
        {
            return new RecordWriteResult(RecordWriteOutcome.Invalid, Errors: failures);
        }

        return await UpdateAsync(connection, application, entity, stored.Id, stored.Version, computed.Values, computed.Rows, cancellationToken);
    }

    /// <summary>
    /// Locks the row of the record with <paramref name="id"/> until the caller's transaction ends,
    /// so a read after it sees the latest version and a concurrent write waits. Returns
    /// <see langword="false"/> when there is no such record.
    /// </summary>
    public static async Task<bool> LockAsync(NpgsqlConnection connection, EntityModel entity, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(entity);

        await using var command = new NpgsqlCommand(
            $"SELECT 1 FROM {RecordQueries.Table(entity)} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id FOR UPDATE",
            connection);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    /// <summary>
    /// Deletes the record with <paramref name="id"/> and returns the version it had. A record that
    /// another record references is kept and is <see cref="RecordDeleteOutcome.Referenced"/>.
    /// </summary>
    public static async Task<RecordDeleteResult> DeleteAsync(
        NpgsqlConnection connection,
        EntityModel entity,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(entity);

        await using var command = new NpgsqlCommand(
            $"DELETE FROM {RecordQueries.Table(entity)} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id RETURNING {EntityNaming.Quote(EntityNaming.VersionColumn)}",
            connection);
        command.Parameters.AddWithValue("id", id);
        try
        {
            return await command.ExecuteScalarAsync(cancellationToken) is long version
                ? new RecordDeleteResult(RecordDeleteOutcome.Deleted, version)
                : new RecordDeleteResult(RecordDeleteOutcome.NotFound);
        }
        // The violated constraint belongs to the referencing table, which may be another entity.
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return new RecordDeleteResult(RecordDeleteOutcome.Referenced);
        }
    }

    /// <summary>
    /// Adds the next number of each sequence field to <paramref name="values"/>, which never
    /// holds one: the parser refuses it. The UTC year is read once, so the period a number is
    /// counted in and the year it shows are the same.
    /// </summary>
    private static async Task<IReadOnlyList<RecordValue>> WithSequenceNumbersAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        IReadOnlyList<RecordValue> values,
        CancellationToken cancellationToken)
    {
        var sequenceFields = RecordQueries.Columns(entity).Where(field => field.Sequence is not null).ToList();
        if (sequenceFields.Count == 0)
        {
            return values;
        }

        var now = DateTimeOffset.UtcNow;
        var numbered = new List<RecordValue>(values);
        foreach (var field in sequenceFields)
        {
            var number = await SequenceCounters.NextAsync(connection, application.Manifest.Id, field.Sequence!, now, cancellationToken);
            numbered.Add(new RecordValue(field, number));
        }

        return numbered;
    }

    /// <summary>
    /// After the owner's write, inserts the rows of each collection in <paramref name="rows"/> one
    /// statement per row, so a violation is keyed by the row's index. With
    /// <paramref name="replace"/>, the stored rows of each collection are deleted first. Returns the
    /// record with the rows of every collection, or the first row's violation.
    /// </summary>
    private static async Task<RecordWriteResult> WriteRowsAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        RecordWriteResult owner,
        IReadOnlyList<RecordRows> rows,
        bool replace,
        CancellationToken cancellationToken)
    {
        if (owner is not { Outcome: RecordWriteOutcome.Written, Record: { } record })
        {
            return owner;
        }

        var ownerColumn = EntityNaming.Quote(EntityNaming.OwnerColumn);
        foreach (var collection in rows)
        {
            var table = RecordQueries.Table(collection.Child);
            if (replace)
            {
                await using var delete = new NpgsqlCommand($"DELETE FROM {table} WHERE {ownerColumn} = @owner", connection);
                delete.Parameters.AddWithValue("owner", record.Id);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            for (var position = 0; position < collection.Rows.Count; position++)
            {
                var values = collection.Rows[position];
                await using var insert = new NpgsqlCommand { Connection = connection };
                insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
                insert.Parameters.AddWithValue("owner", record.Id);
                insert.Parameters.AddWithValue("position", position);
                var columns = new List<string>
                {
                    EntityNaming.Quote(EntityNaming.IdColumn),
                    ownerColumn,
                    EntityNaming.Quote(EntityNaming.PositionColumn),
                };
                var placeholders = new List<string> { "@id", "@owner", "@position" };
                for (var index = 0; index < values.Count; index++)
                {
                    columns.Add(EntityNaming.Quote(EntityNaming.Column(values[index].Field.Name)));
                    placeholders.Add(AddValue(insert, index, values[index]));
                }

                insert.CommandText = $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})";
                try
                {
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (PostgresException exception) when (MapRowViolation(exception, collection, position) is { } result)
                {
                    return result;
                }
            }
        }

        return owner with { Record = await RecordQueries.WithRowsAsync(connection, application, entity, record, cancellationToken) };
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
        catch (PostgresException exception) when (MapViolation(exception, entity, ValuesPointer) is { } result)
        {
            return result;
        }
    }

    /// <summary>
    /// Maps a violation in a row of a child collection like one of the owner, keyed by the row's
    /// pointer, such as <c>/values/parts/1/</c>. A child has no reference field, so a foreign key
    /// the row violates is not one the model declares for the row: a schema conflict.
    /// </summary>
    private static RecordWriteResult? MapRowViolation(PostgresException exception, RecordRows collection, int position) =>
        exception.SqlState == PostgresErrorCodes.ForeignKeyViolation
            ? new RecordWriteResult(RecordWriteOutcome.SchemaConflict)
            : MapViolation(exception, collection.Child, string.Create(CultureInfo.InvariantCulture, $"{ValuesPointer}{collection.Collection.Name}/{position}/"));

    /// <summary>
    /// Maps a constraint violation to its result by the constraint names the model declares, with
    /// error keys under <paramref name="pointer"/>. The exception's column and constraint names
    /// never reach the result.
    /// </summary>
    private static RecordWriteResult? MapViolation(PostgresException exception, EntityModel entity, string pointer)
    {
        var table = EntityNaming.Table(entity.Id);
        switch (exception.SqlState)
        {
            // The pre-check missed a target removed before the write; the foreign key is the backstop.
            case PostgresErrorCodes.ForeignKeyViolation:
                var reference = entity.Fields.FirstOrDefault(field =>
                    field.Type == FieldType.Reference
                    && string.Equals(exception.ConstraintName, EntityNaming.ForeignKey(table, EntityNaming.Column(field.Name)), StringComparison.Ordinal));
                return reference is null ? null : Failure(RecordWriteOutcome.MissingReference, pointer + reference.Name, Messages.MissingReference);
            case PostgresErrorCodes.UniqueViolation:
                var unique = entity.Fields.FirstOrDefault(field =>
                    field.Unique
                    && string.Equals(exception.ConstraintName, EntityNaming.Unique(table, EntityNaming.Column(field.Name)), StringComparison.Ordinal));
                return unique is null
                    ? new RecordWriteResult(RecordWriteOutcome.SchemaConflict)
                    : Failure(RecordWriteOutcome.UniqueViolation, pointer + unique.Name, Messages.NotUnique);
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

    internal static async Task<bool> ExistsAsync(NpgsqlConnection connection, string table, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT EXISTS (SELECT 1 FROM {table} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id)",
            connection);
        command.Parameters.AddWithValue("id", id);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>
    /// Reads the version of the record with <paramref name="id"/> and whether any of
    /// <paramref name="values"/> differs from its stored value; <see langword="null"/> when there
    /// is no record. PostgreSQL compares the typed values, so a decimal stored as <c>1.50</c>
    /// equals a value of <c>1.5</c>.
    /// </summary>
    internal static async Task<(long Version, bool Differs)?> FindVersionAsync(
        NpgsqlConnection connection,
        EntityModel entity,
        Guid id,
        IReadOnlyList<RecordValue> values,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand { Connection = connection };
        command.Parameters.AddWithValue("id", id);
        var comparisons = new List<string>();
        for (var index = 0; index < values.Count; index++)
        {
            comparisons.Add($"{EntityNaming.Quote(EntityNaming.Column(values[index].Field.Name))} IS DISTINCT FROM {AddValue(command, index, values[index])}");
        }

        var differs = comparisons.Count > 0 ? string.Join(" OR ", comparisons) : "false";
        command.CommandText =
            $"SELECT {EntityNaming.Quote(EntityNaming.VersionColumn)}, ({differs}) FROM {RecordQueries.Table(entity)} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetInt64(0), reader.GetBoolean(1))
            : null;
    }

    /// <summary>
    /// Adds the value as parameter <c>p</c> and its index, typed so that a null binds as a typed
    /// <c>NULL</c>, and returns its SQL placeholder. A decimal is text cast to <c>numeric</c>.
    /// </summary>
    internal static string AddValue(NpgsqlCommand command, int index, RecordValue value)
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

    private static RecordWriteResult Failure(RecordWriteOutcome outcome, string key, string message) =>
        new(outcome, Errors: new SortedDictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] });
}
