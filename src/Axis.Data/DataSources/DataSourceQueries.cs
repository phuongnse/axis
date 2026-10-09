using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Expressions.Sql;
using Axis.Expressions.Typing;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.DataSources;

/// <summary>
/// Reads the rows of a data source over a tenant connection. Every identifier comes from
/// <see cref="EntityNaming"/> and is quoted, and every value from the caller or the filter is a
/// parameter. The filter is translated by <see cref="SqlTranslator"/> into the <c>WHERE</c> clause
/// of both the count and the page statement. Its literals are <c>@f0</c>, <c>@f1</c>, … and the
/// data source parameters are <c>@p0</c>, <c>@p1</c>, … in declaration order.
/// </summary>
public static class DataSourceQueries
{
    private const string RowAlias = "r";

    /// <summary>
    /// Reads page <paramref name="page"/> of <paramref name="pageSize"/> rows that pass the filter,
    /// ordered by the sort field and then by the root id ascending, or by the root id alone without
    /// a sort, and counts every row that passes. <paramref name="parameters"/> holds one value per
    /// declared parameter, in declaration order. A page past the last one has no items. Returns
    /// <see langword="null"/> when the database rejects the filter for these rows with a data
    /// exception (SQLSTATE class 22), such as an integer overflow or a date out of range. Any other
    /// database error is thrown.
    /// </summary>
    public static async Task<DataSourceRowPage?> ListAsync(
        NpgsqlConnection connection,
        DataSourceModel dataSource,
        int page,
        int pageSize,
        DataSourceSort? sort,
        IReadOnlyList<DataSourceParameterValue> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var table = EntityNaming.QualifiedTable(EntityNaming.Table(dataSource.Entity.Id));
        var row = EntityNaming.Quote(RowAlias);
        var (where, values) = Where(dataSource);
        var id = $"{row}.{EntityNaming.Quote(EntityNaming.IdColumn)}";
        var order = sort is null
            ? $"{id} ASC"
            : $"{Column(sort.Field.Field)} {(sort.Descending ? "DESC" : "ASC")}, {id} ASC";

        try
        {
            long totalCount;
            await using (var count = new NpgsqlCommand($"SELECT count(*) FROM {table} AS {row}{where}", connection))
            {
                AddValues(count, values);
                AddParameters(count, parameters);
                totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
            }

            await using var command = new NpgsqlCommand(
                $"SELECT {SelectList(dataSource)} FROM {table} AS {row}{LabelJoins(dataSource)}{where} ORDER BY {order} LIMIT @limit OFFSET @offset",
                connection);
            AddValues(command, values);
            AddParameters(command, parameters);
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
        catch (PostgresException exception) when (exception.SqlState.StartsWith("22", StringComparison.Ordinal))
        {
            // Class 22 is a data exception: the filter could not be evaluated for some row.
            return null;
        }
    }

    /// <summary>
    /// The <c>WHERE</c> clause of the filter, with a leading space, and the values it names. Both
    /// are empty without a filter. A bare name in the filter is a parameter, matched ignoring letter
    /// case, or else a column of the root row.
    /// </summary>
    private static (string Where, IReadOnlyList<SqlValue> Values) Where(DataSourceModel dataSource)
    {
        if (dataSource.Filter is not { } filter)
        {
            return ("", []);
        }

        var translated = SqlTranslator.Translate(
            filter.Syntax,
            name =>
            {
                for (var index = 0; index < dataSource.Parameters.Count; index++)
                {
                    if (string.Equals(dataSource.Parameters[index].Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"@{ParameterName(index)}";
                    }
                }

                return $"{EntityNaming.Quote(RowAlias)}.{EntityNaming.Quote(EntityNaming.Column(name))}";
            });
        if (!translated.Succeeded)
        {
            throw new InvalidOperationException($"The compiler let through a filter outside the SQL subset: {translated.Diagnostic.Message}");
        }

        return ($" WHERE {translated.Sql}", translated.Parameters);
    }

    /// <summary>Adds each filter value as a parameter of its exact PostgreSQL type, so no value is ever inferred from text.</summary>
    private static void AddValues(NpgsqlCommand command, IReadOnlyList<SqlValue> values)
    {
        foreach (var value in values)
        {
            var (type, parameterValue) = value.Kind switch
            {
                ExpressionTypeKind.Text => (NpgsqlDbType.Text, value.Value),
                ExpressionTypeKind.Integer => (NpgsqlDbType.Bigint, value.Value),
                ExpressionTypeKind.Decimal => (NpgsqlDbType.Numeric, value.Value),
                ExpressionTypeKind.Boolean => (NpgsqlDbType.Boolean, value.Value),
                ExpressionTypeKind.Date => (NpgsqlDbType.Date, value.Value),
                ExpressionTypeKind.DateTime => (NpgsqlDbType.TimestampTz, ((DateTimeOffset)value.Value).UtcDateTime),
                _ => throw new ArgumentOutOfRangeException(nameof(values), value.Kind, "No SQL parameter type for this kind."),
            };
            command.Parameters.Add(new NpgsqlParameter(value.Name, type) { Value = parameterValue });
        }
    }

    /// <summary>
    /// Adds each data source parameter as <c>@p0</c>, <c>@p1</c>, … with the PostgreSQL type of the
    /// parameter, so a parameter that was not given is a typed NULL that compares with its column.
    /// </summary>
    private static void AddParameters(NpgsqlCommand command, IReadOnlyList<DataSourceParameterValue> parameters)
    {
        for (var index = 0; index < parameters.Count; index++)
        {
            var (parameter, value) = parameters[index];
            var type = parameter.Type switch
            {
                FieldType.Text or FieldType.Enum => NpgsqlDbType.Text,
                FieldType.Integer => NpgsqlDbType.Bigint,
                FieldType.Decimal => NpgsqlDbType.Numeric,
                FieldType.Boolean => NpgsqlDbType.Boolean,
                FieldType.Date => NpgsqlDbType.Date,
                FieldType.DateTime => NpgsqlDbType.TimestampTz,
                FieldType.Reference => NpgsqlDbType.Uuid,
                _ => throw new ArgumentOutOfRangeException(nameof(parameters), parameter.Type, "No SQL parameter type for this parameter."),
            };
            var parameterValue = value is DateTimeOffset dateTime ? dateTime.UtcDateTime : value;
            command.Parameters.Add(new NpgsqlParameter(ParameterName(index), type) { Value = parameterValue ?? DBNull.Value });
        }
    }

    private static string ParameterName(int index) => $"p{index}";

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
