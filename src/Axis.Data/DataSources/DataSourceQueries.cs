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
/// data source parameters are <c>@p0</c>, <c>@p1</c>, … in declaration order. Each distinct path
/// through reference fields, from the projection, the labels, the sort or the filter, is one left
/// join on the target's id, so a page is one statement plus the count whatever its size, and a
/// null reference gives null related values without hiding the row.
/// </summary>
public static class DataSourceQueries
{
    private const string RowAlias = "r";

    /// <summary>
    /// Reads page <paramref name="page"/> of <paramref name="pageSize"/> rows that pass the filter,
    /// ordered by the sort field and then by the root id ascending, or by the root id alone without
    /// a sort, and counts every row that passes. <paramref name="parameters"/> holds one value per
    /// declared parameter, in declaration order. <paramref name="application"/> resolves the paths
    /// in the filter. A page past the last one has no items. Returns
    /// <see langword="null"/> when the database rejects the filter for these rows with a data
    /// exception (SQLSTATE class 22), such as an integer overflow or a date out of range. Any other
    /// database error is thrown.
    /// </summary>
    public static async Task<DataSourceRowPage?> ListAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        DataSourceModel dataSource,
        int page,
        int pageSize,
        DataSourceSort? sort,
        IReadOnlyList<DataSourceParameterValue> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var table = EntityNaming.QualifiedTable(EntityNaming.Table(dataSource.Entity.Id));
        var row = EntityNaming.Quote(RowAlias);
        var joins = new Joins();
        var select = SelectList(dataSource, joins);
        var id = $"{row}.{EntityNaming.Quote(EntityNaming.IdColumn)}";
        var order = sort is null
            ? $"{id} ASC"
            : $"{joins.ColumnOf(sort.Field.Path)} {(sort.Descending ? "DESC" : "ASC")}, {id} ASC";
        var (where, values) = Where(application, dataSource, joins);
        var from = $"{table} AS {row}{joins.Sql}{where}";

        try
        {
            long totalCount;
            await using (var count = new NpgsqlCommand($"SELECT count(*) FROM {from}", connection))
            {
                AddValues(count, values);
                AddParameters(count, parameters);
                totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
            }

            await using var command = new NpgsqlCommand(
                $"SELECT {select} FROM {from} ORDER BY {order} LIMIT @limit OFFSET @offset",
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
    /// case, or else a column of the root row. A longer path is a column of a joined target.
    /// </summary>
    private static (string Where, IReadOnlyList<SqlValue> Values) Where(
        ApplicationModel application, DataSourceModel dataSource, Joins joins)
    {
        if (dataSource.Filter is not { } filter)
        {
            return ("", []);
        }

        var translated = SqlTranslator.Translate(
            filter.Syntax,
            names =>
            {
                if (names.Count == 1)
                {
                    for (var index = 0; index < dataSource.Parameters.Count; index++)
                    {
                        if (string.Equals(dataSource.Parameters[index].Name, names[0], StringComparison.OrdinalIgnoreCase))
                        {
                            return $"@{ParameterName(index)}";
                        }
                    }
                }

                return joins.ColumnOf(ResolvePath(application, dataSource, names));
            });
        if (!translated.Succeeded)
        {
            throw new InvalidOperationException($"The compiler let through a filter outside the SQL subset: {translated.Diagnostic.Message}");
        }

        return ($" WHERE {translated.Sql}", translated.Parameters);
    }

    /// <summary>The fields a filter path names, from the root entity through each reference's target, ignoring letter case.</summary>
    private static List<FieldModel> ResolvePath(ApplicationModel application, DataSourceModel dataSource, IReadOnlyList<string> names)
    {
        var path = new List<FieldModel>(names.Count);
        var entityName = dataSource.Entity.Name;
        foreach (var name in names)
        {
            if (entityName is null
                || !application.TryGetEntity(entityName, out var entity)
                || !entity.TryGetField(name, out var field))
            {
                throw new InvalidOperationException(
                    $"The compiler let through the filter path '{string.Join('.', names)}' that does not resolve.");
            }

            path.Add(field);
            entityName = field.Target?.Name;
        }

        return path;
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
    /// lost. Then the display field of each projected reference's target, from its join.
    /// </summary>
    private static string SelectList(DataSourceModel dataSource, Joins joins) =>
        string.Join(", ", [
            $"{EntityNaming.Quote(RowAlias)}.{EntityNaming.Quote(EntityNaming.IdColumn)}",
            .. dataSource.Fields.Select(field =>
                joins.ColumnOf(field.Path) + (field.Field.Type == FieldType.Decimal ? "::text" : "")),
            .. References(dataSource).Select(field =>
                $"{joins.TargetAlias(field.Path)}.{EntityNaming.Quote(EntityNaming.Column(field.Field.TargetDisplayField!))}"),
        ]);

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

    /// <summary>
    /// The left joins of one statement, one per distinct path through reference fields, keyed by
    /// the path's field names ignoring letter case. Joins are aliased <c>j0</c>, <c>j1</c>, … in the
    /// order they are first needed, so a join always follows the join it starts from.
    /// </summary>
    private sealed class Joins
    {
        private readonly Dictionary<string, Join> _joins = new(StringComparer.Ordinal);
        private readonly List<Join> _ordered = [];

        /// <summary>The joins as SQL, each with a leading space. Empty without a join.</summary>
        public string Sql => string.Concat(_ordered.Select(join =>
        {
            var alias = EntityNaming.Quote(join.Alias);
            return $" LEFT JOIN {EntityNaming.QualifiedTable(EntityNaming.Table(join.Target.Id))} AS {alias}"
                + $" ON {alias}.{EntityNaming.Quote(EntityNaming.IdColumn)}"
                + $" = {EntityNaming.Quote(join.ParentAlias)}.{EntityNaming.Quote(EntityNaming.Column(join.Reference.Name))}";
        }));

        /// <summary>The column of the field <paramref name="path"/> ends at, joining each reference before it.</summary>
        public string ColumnOf(IReadOnlyList<FieldModel> path) =>
            $"{EntityNaming.Quote(AliasOf(path, path.Count - 1))}.{EntityNaming.Quote(EntityNaming.Column(path[^1].Name))}";

        /// <summary>The alias of the target of the reference field <paramref name="path"/> ends at, joining every reference of the path.</summary>
        public string TargetAlias(IReadOnlyList<FieldModel> path) => EntityNaming.Quote(AliasOf(path, path.Count));

        /// <summary>The alias of the row reached through the first <paramref name="count"/> references of <paramref name="path"/>.</summary>
        private string AliasOf(IReadOnlyList<FieldModel> path, int count)
        {
            var alias = RowAlias;
            var key = "";
            for (var index = 0; index < count; index++)
            {
                var reference = path[index];
                key += (index == 0 ? "" : ".") + reference.Name.ToLowerInvariant();
                if (!_joins.TryGetValue(key, out var join))
                {
                    join = new Join($"j{_ordered.Count}", reference.Target!, alias, reference);
                    _joins.Add(key, join);
                    _ordered.Add(join);
                }

                alias = join.Alias;
            }

            return alias;
        }

        private sealed record Join(string Alias, EntityReference Target, string ParentAlias, FieldModel Reference);
    }
}
