using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Npgsql;

namespace Axis.Data.Records;

/// <summary>
/// Shows a record whose computed fields are pending as the active release would compute them. A single
/// read, the response of an update and the engine's read of a subject record call
/// <see cref="ApplyAsync"/>: when the entity is pending and the row's stamp is out of date, it works out the
/// computed fields of the record and of its child rows with the active release, on the spot. It stores
/// nothing: no value, no stamp and no row in <c>axis.computed_field_errors</c>. A field that fails reads
/// <c>null</c>.
/// </summary>
public static class PendingComputedReads
{
    private const string DateFormat = "yyyy-MM-dd";

    // PostgreSQL stores microseconds, so six fraction digits show every stored value.
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    /// <summary>
    /// Returns <paramref name="record"/> of <paramref name="entity"/> with its pending computed fields
    /// computed by <paramref name="active"/>, the application's active release, inside
    /// <paramref name="transaction"/> when it is not <see langword="null"/>. The record is returned
    /// unchanged when the entity is not pending, when the row is up to date, or when the entity has no
    /// computed field. <paramref name="entity"/> and <paramref name="record"/> may come from a release the
    /// work is pinned to, and then <paramref name="recordApplication"/> is that release's model. A computed
    /// field of the record is replaced only when the active release has a computed field of the same name
    /// and type, because the work's expressions are typed by its own release. The rows of a child collection
    /// are replaced by position, and only when the stored row count is the one read now. The version and the
    /// labels are kept.
    /// </summary>
    public static async Task<Record> ApplyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ApplicationModel active,
        EntityModel entity,
        Record record,
        CancellationToken cancellationToken,
        ApplicationModel? recordApplication = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(record);

        if (active.FindEntity(entity.Id) is not { } activeEntity)
        {
            return record;
        }

        var collections = ComputedRecompute.CollectionsOf(active, activeEntity);
        if (!activeEntity.HasComputedFields && collections.All(collection => !collection.Child.HasComputedFields))
        {
            return record;
        }

        Guid applicationId;
        Guid storedTarget;
        Guid[] storedCurrent;
        Guid? stamp;
        await using (var read = new NpgsqlCommand(
            $"SELECT p.application_id, p.target_release_id, p.current_release_ids, r.{EntityNaming.Quote(EntityNaming.ComputedReleaseColumn)} "
            + $"FROM axis.pending_computed_fields p JOIN {RecordQueries.Table(activeEntity)} r ON r.{EntityNaming.Quote(EntityNaming.IdColumn)} = @id "
            + "WHERE p.entity_id = @entity",
            connection,
            transaction))
        {
            read.Parameters.AddWithValue("id", record.Id);
            read.Parameters.AddWithValue("entity", entity.Id);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return record;
            }

            applicationId = reader.GetGuid(0);
            storedTarget = reader.GetGuid(1);
            storedCurrent = reader.GetFieldValue<Guid[]>(2);
            stamp = reader.IsDBNull(3) ? null : reader.GetGuid(3);
        }

        var (_, current) = await ComputedBackfill.ResolveTargetAsync(connection, transaction, entity.Id, applicationId, storedTarget, storedCurrent, cancellationToken);
        if (stamp is { } stamped && current.Contains(stamped))
        {
            return record;
        }

        if (await ComputedRecompute.ComputeRecordAsync(connection, transaction, activeEntity, collections, record.Id, cancellationToken) is not { } computed)
        {
            return record;
        }

        var values = new Dictionary<string, JsonNode?>(record.Values, StringComparer.Ordinal);
        foreach (var value in computed.Owner)
        {
            if (Replaceable(entity, value.Field) is { } field)
            {
                values[field.Name] = ToJson(value);
            }
        }

        var recordModel = recordApplication ?? active;
        foreach (var field in entity.Fields.Where(field => field.Type == FieldType.ChildCollection))
        {
            var rows = computed.Children.Where(child => string.Equals(child.Collection, field.Name, StringComparison.Ordinal)).ToList();
            if (rows.Count == 0
                || record.Values.GetValueOrDefault(field.Name) is not JsonArray stored
                || stored.Count != rows.Count
                || recordModel.FindEntity(field.Target!.Id) is not { } child)
            {
                continue;
            }

            var overlaid = new JsonArray();
            for (var index = 0; index < rows.Count; index++)
            {
                var row = (JsonObject)stored[index]!.DeepClone();
                foreach (var value in rows[index].Values)
                {
                    if (Replaceable(child, value.Field) is { } childField)
                    {
                        row[childField.Name] = ToJson(value);
                    }
                }

                overlaid.Add(row);
            }

            values[field.Name] = overlaid;
        }

        return record with { Values = values };
    }

    // The field of the record's own entity that the active release's computed field replaces: the one with the
    // same name and type, which must be computed there too.
    private static FieldModel? Replaceable(EntityModel own, FieldModel active) =>
        own.TryGetField(active.Name, out var field) && field.IsComputed && field.Type == active.Type ? field : null;

    // The value as RecordQueries.ReadValue gives it for a stored column.
    private static JsonNode? ToJson(RecordValue value)
    {
        if (value.Value is null)
        {
            return null;
        }

        return value.Field.Type switch
        {
            FieldType.Text or FieldType.Enum => JsonValue.Create((string)value.Value),
            FieldType.Integer => JsonValue.Create((long)value.Value),
            FieldType.Decimal => JsonNode.Parse(DecimalText(value.Field, (string)value.Value)),
            FieldType.Boolean => JsonValue.Create((bool)value.Value),
            FieldType.Date => JsonValue.Create(((DateOnly)value.Value).ToString(DateFormat, CultureInfo.InvariantCulture)),
            FieldType.DateTime => JsonValue.Create(((DateTimeOffset)value.Value).UtcDateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture)),
            FieldType.Reference => JsonValue.Create(((Guid)value.Value).ToString("D")),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.Field.Type, "A field of this type cannot be computed."),
        };
    }

    // A column with a precision shows exactly its scale in fraction digits, as numeric(p,s)::text does. Otherwise
    // the text stays as computed.
    private static string DecimalText(FieldModel field, string text)
    {
        if (field.Precision is null)
        {
            return text;
        }

        var scale = field.Scale ?? 0;
        var negative = text.StartsWith('-');
        var unsigned = negative ? text[1..] : text;
        var point = unsigned.IndexOf('.', StringComparison.Ordinal);
        var integer = point < 0 ? unsigned : unsigned[..point];
        var fraction = (point < 0 ? "" : unsigned[(point + 1)..]).TrimEnd('0').PadRight(scale, '0');
        var number = scale == 0 ? integer : $"{integer}.{fraction}";
        return negative && number.Any(character => character is >= '1' and <= '9') ? "-" + number : number;
    }
}
