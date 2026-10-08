using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Data.Records;
using Npgsql;

namespace Axis.Data.DataSources;

/// <summary>
/// Reads the rows of a data source over a tenant connection. Every identifier comes from
/// <see cref="EntityNaming"/> and is quoted, and every value from the caller is a parameter.
/// </summary>
public static class DataSourceQueries
{
    private const string RowAlias = "r";

    /// <summary>
    /// Reads page <paramref name="page"/> of <paramref name="pageSize"/> rows, ordered by the sort
    /// field and then by the root id ascending, or by the root id alone without a sort, and counts
    /// every row of the data source. A page past the last one has no items.
    /// </summary>
    public static async Task<DataSourceRowPage> ListAsync(
        NpgsqlConnection connection,
        DataSourceModel dataSource,
        int page,
        int pageSize,
        DataSourceSort? sort,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var table = EntityNaming.QualifiedTable(EntityNaming.Table(dataSource.Entity.Id));
        long totalCount;
        await using (var count = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection))
        {
            totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

        var row = EntityNaming.Quote(RowAlias);
        var id = $"{row}.{EntityNaming.Quote(EntityNaming.IdColumn)}";
        var order = sort is null
            ? $"{id} ASC"
            : $"{Column(sort.Field.Field)} {(sort.Descending ? "DESC" : "ASC")}, {id} ASC";
        await using var command = new NpgsqlCommand(
            $"SELECT {SelectList(dataSource)} FROM {table} AS {row}{LabelJoins(dataSource)} ORDER BY {order} LIMIT @limit OFFSET @offset",
            connection);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (long)(page - 1) * pageSize);

        var items = new List<DataSourceRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadRow(reader, dataSource));
        }

        return new DataSourceRowPage(items, totalCount);
    }

    /// <summary>
    /// The root id, then every projected field in projection order, decimals as text so no digit is
    /// lost. Then the display field of each projected reference's target, joined by <see cref="LabelJoins"/>.
    /// </summary>
    private static string SelectList(DataSourceModel dataSource) =>
        string.Join(", ", [
            $"{EntityNaming.Quote(RowAlias)}.{EntityNaming.Quote(EntityNaming.IdColumn)}",
            .. dataSource.Fields.Select(field => Column(field.Field) + (field.Field.Type == FieldType.Decimal ? "::text" : "")),
            .. References(dataSource).Select((field, index) =>
                $"{EntityNaming.Quote(LabelAlias(index))}.{EntityNaming.Quote(EntityNaming.Column(field.Field.TargetDisplayField!))}"),
        ]);

    /// <summary>
    /// One left join per projected reference field to the target table, aliased by the reference's
    /// position, so each row's labels come from the same statement. Empty without a projected reference.
    /// </summary>
    private static string LabelJoins(DataSourceModel dataSource) =>
        string.Concat(References(dataSource).Select((field, index) =>
        {
            var target = EntityNaming.Quote(LabelAlias(index));
            return $" LEFT JOIN {EntityNaming.QualifiedTable(EntityNaming.Table(field.Field.Target!.Id))} AS {target}"
                + $" ON {target}.{EntityNaming.Quote(EntityNaming.IdColumn)} = {Column(field.Field)}";
        }));

    private static string Column(FieldModel field) =>
        $"{EntityNaming.Quote(RowAlias)}.{EntityNaming.Quote(EntityNaming.Column(field.Name))}";

    private static string LabelAlias(int index) => $"l{index}";

    private static IEnumerable<DataSourceFieldModel> References(DataSourceModel dataSource) =>
        dataSource.Fields.Where(field => field.Field.Type == FieldType.Reference);

    private static DataSourceRow ReadRow(NpgsqlDataReader reader, DataSourceModel dataSource)
    {
        var values = new Dictionary<string, JsonValue?>(dataSource.Fields.Count, StringComparer.Ordinal);
        for (var index = 0; index < dataSource.Fields.Count; index++)
        {
            var field = dataSource.Fields[index];
            var ordinal = index + 1;
            values[field.Name] = reader.IsDBNull(ordinal) ? null : RecordQueries.ReadValue(reader, ordinal, field.Field);
        }

        // A null reference, or a target whose display column is NULL, has no label.
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var labelOrdinal = dataSource.Fields.Count + 1;
        foreach (var field in References(dataSource))
        {
            if (!reader.IsDBNull(labelOrdinal))
            {
                labels[field.Name] = reader.GetString(labelOrdinal);
            }

            labelOrdinal++;
        }

        return new DataSourceRow(reader.GetGuid(0), values, labels);
    }
}
