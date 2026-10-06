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

        var id = EntityNaming.Quote(EntityNaming.IdColumn);
        var order = sort is null
            ? $"{id} ASC"
            : $"{EntityNaming.Quote(EntityNaming.Column(sort.Field.Name))} {(sort.Descending ? "DESC" : "ASC")}, {id} ASC";
        await using var command = new NpgsqlCommand(
            $"SELECT {SelectList(entity)} FROM {table} ORDER BY {order} LIMIT @limit OFFSET @offset",
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

    /// <summary>Reads the record with <paramref name="id"/>, or <see langword="null"/> when there is none.</summary>
    public static async Task<Record?> GetAsync(
        NpgsqlConnection connection,
        EntityModel entity,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(entity);

        await using var command = new NpgsqlCommand(
            $"SELECT {SelectList(entity)} FROM {Table(entity)} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id",
            connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader, entity) : null;
    }

    internal static string Table(EntityModel entity) => EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id));

    /// <summary>The id, the version, then every field in declaration order; decimals as text so no digit is lost.</summary>
    internal static string SelectList(EntityModel entity) =>
        string.Join(", ", [
            EntityNaming.Quote(EntityNaming.IdColumn),
            EntityNaming.Quote(EntityNaming.VersionColumn),
            .. entity.Fields.Select(field =>
                EntityNaming.Quote(EntityNaming.Column(field.Name)) + (field.Type == FieldType.Decimal ? "::text" : "")),
        ]);

    internal static Record ReadRecord(NpgsqlDataReader reader, EntityModel entity)
    {
        var values = new Dictionary<string, JsonValue?>(entity.Fields.Count, StringComparer.Ordinal);
        for (var index = 0; index < entity.Fields.Count; index++)
        {
            var field = entity.Fields[index];
            var ordinal = index + 2;
            values[field.Name] = reader.IsDBNull(ordinal) ? null : ReadValue(reader, ordinal, field);
        }

        return new Record(reader.GetGuid(0), reader.GetInt64(1), values);
    }

    private static JsonValue ReadValue(NpgsqlDataReader reader, int ordinal, FieldModel field) =>
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
