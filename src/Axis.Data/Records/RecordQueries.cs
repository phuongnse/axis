using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Npgsql;

namespace Axis.Data.Records;

/// <summary>
/// Reads records of an entity table over a tenant connection. Every identifier comes from
/// <see cref="EntityNaming"/> and is quoted, and every value from the caller is a parameter.
/// </summary>
public static class RecordQueries
{
    private const string DateFormat = "yyyy-MM-dd";

    // PostgreSQL stores microseconds, so six fraction digits show every stored value exactly.
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    /// <summary>The alias of the entity table in a read. Joined targets have their own id and version columns.</summary>
    internal const string RowAlias = "r";

    /// <summary>
    /// Reads page <paramref name="page"/> of <paramref name="pageSize"/> records, ordered by the
    /// sort field and then by id ascending, or by id alone without a sort, and counts every record
    /// of the entity. A page past the last one has no items.
    /// </summary>
    public static async Task<RecordPage> ListAsync(
        NpgsqlConnection connection,
        EntityModel entity,
        int page,
        int pageSize,
        RecordSort? sort,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var table = Table(entity);
        long totalCount;
        await using (var count = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection))
        {
            totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

        var row = EntityNaming.Quote(RowAlias);
        var id = $"{row}.{EntityNaming.Quote(EntityNaming.IdColumn)}";
        var order = sort is null
            ? $"{id} ASC"
            : $"{row}.{EntityNaming.Quote(EntityNaming.Column(sort.Field.Name))} {(sort.Descending ? "DESC" : "ASC")}, {id} ASC";
        await using var command = new NpgsqlCommand(
            $"SELECT {SelectList(entity, RowAlias)} FROM {table} AS {row}{LabelJoins(entity, RowAlias)} ORDER BY {order} LIMIT @limit OFFSET @offset",
            connection);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (long)(page - 1) * pageSize);

        var items = new List<Record>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadRecord(reader, entity));
        }

        return new RecordPage(items, totalCount);
    }

    /// <summary>
    /// Reads the record with <paramref name="id"/> and the rows of its child collections, or
    /// <see langword="null"/> when there is none. <paramref name="application"/> declares the child entities.
    /// </summary>
    public static async Task<Record?> GetAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);

        var row = EntityNaming.Quote(RowAlias);
        Record? record;
        await using (var command = new NpgsqlCommand(
            $"SELECT {SelectList(entity, RowAlias)} FROM {Table(entity)} AS {row}{LabelJoins(entity, RowAlias)} WHERE {row}.{EntityNaming.Quote(EntityNaming.IdColumn)} = @id",
            connection))
        {
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            record = await reader.ReadAsync(cancellationToken) ? ReadRecord(reader, entity) : null;
        }

        return record is null ? null : await WithRowsAsync(connection, application, entity, record, cancellationToken);
    }

    /// <summary>
    /// Adds the rows of each child collection of <paramref name="entity"/> to
    /// <paramref name="record"/>, in position order, with one query per collection. The values
    /// keep the entity's field declaration order.
    /// </summary>
    internal static async Task<Record> WithRowsAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        Record record,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, JsonNode?>(entity.Fields.Count, StringComparer.Ordinal);
        foreach (var field in entity.Fields)
        {
            values[field.Name] = field.Type == FieldType.ChildCollection
                ? await ReadRowsAsync(connection, application, field, record.Id, cancellationToken)
                : record.Values[field.Name];
        }

        return record with { Values = values };
    }

    private static async Task<JsonArray> ReadRowsAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        FieldModel collection,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var child = application.FindEntity(collection.Target!.Id)
            ?? throw new InvalidOperationException("The application has no entity for the child collection.");
        var columns = Columns(child);
        var selected = columns.Select(field =>
            EntityNaming.Quote(EntityNaming.Column(field.Name)) + (field.Type == FieldType.Decimal ? "::text" : ""));
        await using var command = new NpgsqlCommand(
            $"SELECT {string.Join(", ", selected)} FROM {Table(child)} WHERE {EntityNaming.Quote(EntityNaming.OwnerColumn)} = @owner ORDER BY {EntityNaming.Quote(EntityNaming.PositionColumn)}",
            connection);
        command.Parameters.AddWithValue("owner", ownerId);

        var rows = new JsonArray();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new JsonObject();
            for (var ordinal = 0; ordinal < columns.Count; ordinal++)
            {
                row[columns[ordinal].Name] = reader.IsDBNull(ordinal) ? null : ReadValue(reader, ordinal, columns[ordinal]);
            }

            rows.Add(row);
        }

        return rows;
    }

    internal static string Table(EntityModel entity) => EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id));

    /// <summary>
    /// The id, the version, then every column field in declaration order, all of the row named
    /// <paramref name="alias"/>; decimals as text so no digit is lost. Then the display field of
    /// each reference's target, joined by <see cref="LabelJoins"/>.
    /// </summary>
    internal static string SelectList(EntityModel entity, string alias)
    {
        var row = EntityNaming.Quote(alias) + ".";
        return string.Join(", ", [
            row + EntityNaming.Quote(EntityNaming.IdColumn),
            row + EntityNaming.Quote(EntityNaming.VersionColumn),
            .. Columns(entity).Select(field =>
                row + EntityNaming.Quote(EntityNaming.Column(field.Name)) + (field.Type == FieldType.Decimal ? "::text" : "")),
            .. References(entity).Select((field, index) =>
                $"{EntityNaming.Quote(LabelAlias(index))}.{EntityNaming.Quote(EntityNaming.Column(field.TargetDisplayField!))}"),
        ]);
    }

    /// <summary>
    /// One left join per reference field to the target table, aliased by the reference's position,
    /// so each record's labels come from the same statement. Empty without a reference field.
    /// </summary>
    internal static string LabelJoins(EntityModel entity, string alias) =>
        string.Concat(References(entity).Select((field, index) =>
        {
            var target = EntityNaming.Quote(LabelAlias(index));
            return $" LEFT JOIN {EntityNaming.QualifiedTable(EntityNaming.Table(field.Target!.Id))} AS {target}"
                + $" ON {target}.{EntityNaming.Quote(EntityNaming.IdColumn)} = {EntityNaming.Quote(alias)}.{EntityNaming.Quote(EntityNaming.Column(field.Name))}";
        }));

    private static string LabelAlias(int index) => $"l{index}";

    private static IEnumerable<FieldModel> References(EntityModel entity) =>
        Columns(entity).Where(field => field.Type == FieldType.Reference);

    /// <summary>
    /// The fields stored in a column of the entity table, in declaration order. A child collection
    /// has none: its rows are read and written in the child table.
    /// </summary>
    internal static IReadOnlyList<FieldModel> Columns(EntityModel entity) =>
        [.. entity.Fields.Where(field => field.HasColumn)];

    internal static Record ReadRecord(NpgsqlDataReader reader, EntityModel entity)
    {
        var columns = Columns(entity);
        var values = new Dictionary<string, JsonNode?>(columns.Count, StringComparer.Ordinal);
        for (var index = 0; index < columns.Count; index++)
        {
            var field = columns[index];
            var ordinal = index + 2;
            values[field.Name] = reader.IsDBNull(ordinal) ? null : ReadValue(reader, ordinal, field);
        }

        // A null reference, or a target whose display column is NULL, has no label.
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var labelOrdinal = columns.Count + 2;
        foreach (var field in References(entity))
        {
            if (!reader.IsDBNull(labelOrdinal))
            {
                labels[field.Name] = reader.GetString(labelOrdinal);
            }

            labelOrdinal++;
        }

        return new Record(reader.GetGuid(0), reader.GetInt64(1), values, labels);
    }

    internal static JsonValue ReadValue(NpgsqlDataReader reader, int ordinal, FieldModel field) =>
        field.Type switch
        {
            FieldType.Text or FieldType.Enum => JsonValue.Create(reader.GetString(ordinal)),
            FieldType.Integer => JsonValue.Create(reader.GetInt64(ordinal)),
            // The number text is written as is, so trailing zeros and every digit are kept.
            FieldType.Decimal => (JsonValue)JsonNode.Parse(reader.GetString(ordinal))!,
            FieldType.Boolean => JsonValue.Create(reader.GetBoolean(ordinal)),
            FieldType.Date => JsonValue.Create(reader.GetFieldValue<DateOnly>(ordinal).ToString(DateFormat, CultureInfo.InvariantCulture)),
            // Npgsql reads timestamp with time zone as a UTC DateTime.
            FieldType.DateTime => JsonValue.Create(reader.GetFieldValue<DateTime>(ordinal).ToString(DateTimeFormat, CultureInfo.InvariantCulture)),
            FieldType.Reference => JsonValue.Create(reader.GetGuid(ordinal).ToString("D")),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field.Type, "Unknown field type."),
        };
}
