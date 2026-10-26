using System.Runtime.CompilerServices;
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
/// of both the count and the page statement, with the body of each rule it calls inlined. Its
/// literals are <c>@f0</c>, <c>@f1</c>, … and the data source parameters are <c>@p0</c>,
/// <c>@p1</c>, … in declaration order. The filter of measure <c>i</c> has its own literals,
/// <c>@m{i}_0</c>, <c>@m{i}_1</c>, …. A group that buckets a date-time reads the application time
/// zone from the text parameter <c>@tz</c>, which only the count and page statements of such a data
/// source carry. Each distinct path
/// through reference fields, from the projection, the labels, the sort or the filter, is one left
/// join on the target's id, so a page is one statement plus the count whatever its size, and a
/// null reference gives null related values without hiding the row. A grouped data source groups
/// the filtered rows by its group fields, and each row of a page is one group. A bucketed group
/// field is the first date of the bucket that holds its value. A measure with a filter is
/// <c>agg(...) FILTER (WHERE ...)</c>.
/// </summary>
public static class DataSourceQueries
{
    private const string RowAlias = "r";

    private const string CountSavepoint = "count";

    /// <summary>
    /// Reads page <paramref name="page"/> of <paramref name="pageSize"/> rows that pass the filter,
    /// ordered by the sort field and then by the root id ascending, or by the root id alone without
    /// a sort, and counts every row that passes. When the data source is grouped, a row is one group
    /// of the rows that pass, ordered by the sort and then by each group field ascending, and the
    /// count is the number of groups. <paramref name="parameters"/> holds one value per
    /// declared parameter, in declaration order. <paramref name="application"/> resolves the paths
    /// in the filter. When <paramref name="condition"/> is given, a root row passes only when it
    /// also matches it, in both statements and before any grouping, so the rows, the count and
    /// every measure cover only those root records. When <paramref name="search"/> is not empty and the
    /// data source has search fields, a root row passes only when one of them contains it, ignoring
    /// letter case, also before any grouping. A page past the last one has no items. Returns
    /// <see langword="null"/> when the database rejects the filter or the condition for these rows
    /// with a data exception (SQLSTATE class 22), such as an integer overflow or a date out of
    /// range. Any other database error is thrown.
    /// When <paramref name="transaction"/>, the transaction of <paramref name="connection"/>, is given,
    /// the count runs under a savepoint. A count that the database cancels at the statement timeout
    /// (SQLSTATE 57014) then leaves <see cref="DataSourceRowPage.TotalCount"/> null, and the page is still
    /// read. Without a transaction that error is thrown.
    /// </summary>
    public static async Task<DataSourceRowPage?> ListAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ApplicationModel application,
        DataSourceModel dataSource,
        int page,
        int pageSize,
        DataSourceSort? sort,
        IReadOnlyList<DataSourceParameterValue> parameters,
        RecordCondition? condition = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var searching = !string.IsNullOrEmpty(search) && dataSource.SearchFields.Count > 0;
        var statement = Statement(application, dataSource, sort, condition, searching);
        try
        {
            long? totalCount = null;
            if (transaction is not null)
            {
                await transaction.SaveAsync(CountSavepoint, cancellationToken);
            }

            try
            {
                await using var count = new NpgsqlCommand(statement.CountSql, connection);
                AddValues(count, statement.Values);
                AddParameters(count, parameters);
                AddTimeZone(count, statement);
                condition?.AddTo(count);
                if (searching)
                {
                    count.Parameters.Add(RecordQueries.SearchParameter(search!));
                }

                totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
                if (transaction is not null)
                {
                    await transaction.ReleaseAsync(CountSavepoint, cancellationToken);
                }
            }
            catch (PostgresException exception) when (transaction is not null && exception.SqlState == PostgresErrorCodes.QueryCanceled)
            {
                // The count alone ran too long, so the page still answers without a total.
                await transaction.RollbackAsync(CountSavepoint, cancellationToken);
                await transaction.ReleaseAsync(CountSavepoint, cancellationToken);
            }

            await using var command = new NpgsqlCommand(statement.PageSql, connection);
            AddValues(command, statement.Values);
            AddParameters(command, parameters);
            AddTimeZone(command, statement);
            condition?.AddTo(command);
            if (searching)
            {
                command.Parameters.Add(RecordQueries.SearchParameter(search!));
            }

            command.Parameters.AddWithValue("limit", pageSize);
            command.Parameters.AddWithValue("offset", (long)(page - 1) * pageSize);

            var items = new List<DataSourceRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(dataSource.Aggregate is { } grouped ? ReadGroupedRow(reader, grouped) : ReadRow(reader, dataSource));
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
    /// Streams up to <paramref name="limit"/> rows in the order, filter and condition of
    /// <see cref="ListAsync"/>, one at a time, so a caller never holds them all. A grouped data source
    /// streams its groups. It does not count. A database error is thrown as it is, including a data
    /// exception (SQLSTATE class 22) that the filter or the condition raises for some row, which the
    /// caller maps.
    /// </summary>
    public static async IAsyncEnumerable<DataSourceRow> ReadAllAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        DataSourceModel dataSource,
        DataSourceSort? sort,
        IReadOnlyList<DataSourceParameterValue> parameters,
        RecordCondition? condition,
        int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var statement = Statement(application, dataSource, sort, condition);
        await using var command = new NpgsqlCommand(statement.PageSql, connection);
        AddValues(command, statement.Values);
        AddParameters(command, parameters);
        AddTimeZone(command, statement);
        condition?.AddTo(command);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("offset", 0L);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return dataSource.Aggregate is { } grouped ? ReadGroupedRow(reader, grouped) : ReadRow(reader, dataSource);
        }
    }

    /// <summary>
    /// Reads up to <paramref name="limit"/> root ids of the rows that pass the filter, in id order,
    /// starting after <paramref name="after"/> when it is given. Unlike <see cref="ListAsync"/> it
    /// reads by id and not by page, so rows added or removed between two calls never shift it, and
    /// it does not count. <paramref name="parameters"/> holds one value per declared parameter, in
    /// declaration order. Returns <see langword="null"/> when the database rejects the filter with
    /// a data exception (SQLSTATE class 22). Any other database error is thrown.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>?> ListIdsAfterAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        DataSourceModel dataSource,
        IReadOnlyList<DataSourceParameterValue> parameters,
        Guid? after,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var id = $"{EntityNaming.Quote(RowAlias)}.{EntityNaming.Quote(EntityNaming.IdColumn)}";
        var cursor = after is null ? null : new RecordCondition($"({id} > @after)", [], "");
        var statement = Statement(application, dataSource, sort: null, cursor);
        try
        {
            await using var command = new NpgsqlCommand($"SELECT {id} FROM {statement.From} ORDER BY {id} ASC LIMIT @limit", connection);
            AddValues(command, statement.Values);
            AddParameters(command, parameters);
            if (after is { } last)
            {
                command.Parameters.Add(new NpgsqlParameter("after", NpgsqlDbType.Uuid) { Value = last });
            }

            command.Parameters.AddWithValue("limit", limit);
            var ids = new List<Guid>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetGuid(0));
            }

            return ids;
        }
        catch (PostgresException exception) when (exception.SqlState.StartsWith("22", StringComparison.Ordinal))
        {
            // Class 22 is a data exception: the filter could not be evaluated for some row.
            return null;
        }
    }

    /// <summary>
    /// The count and the page statement of <paramref name="dataSource"/>, and the values of its
    /// filter, then of each measure filter. A measure filter is translated once, and the select list
    /// and the sort both use its SQL. The page statement ends with <c>LIMIT @limit OFFSET @offset</c>. Both put the
    /// filter and <paramref name="condition"/>, joined with <c>AND</c>, in one <c>WHERE</c> clause
    /// over the root row before any grouping. When <paramref name="search"/> is set, they also hold
    /// one term per search field, joined with <c>OR</c>, that matches <c>@search</c>. Neither holds a
    /// value from a filter, a parameter, the condition or the search: each is a parameter that the caller adds.
    /// The count statement names no measure filter, so it ignores their values.
    /// </summary>
    internal static DataSourceStatement Statement(
        ApplicationModel application, DataSourceModel dataSource, DataSourceSort? sort, RecordCondition? condition, bool search = false)
    {
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(dataSource.Entity.Id));
        var row = EntityNaming.Quote(RowAlias);
        var joins = new Joins();
        string select, groupBy, order;
        var measureValues = new List<SqlValue>();
        if (dataSource.Aggregate is { } aggregate)
        {
            // Keyed by measure name, which is unique in a data source.
            var measureFilters = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < aggregate.Measures.Count; index++)
            {
                if (aggregate.Measures[index].Filter is { } measureFilter)
                {
                    var (sql, values) = Translate(application, dataSource, measureFilter, joins, $"m{index}_");
                    measureFilters.Add(aggregate.Measures[index].Name, sql);
                    measureValues.AddRange(values);
                }
            }

            (select, groupBy) = GroupedSelectList(aggregate, joins, measureFilters);

            // Each group is unique by its group fields, so they break every tie.
            var groupOrder = aggregate.GroupBy.Select(field => $"{GroupColumn(field, joins)} ASC");
            order = string.Join(", ", sort is null ? groupOrder : [OrderTerm(sort, joins, measureFilters), .. groupOrder]);
        }
        else
        {
            select = SelectList(dataSource, joins);
            groupBy = "";
            var id = $"{row}.{EntityNaming.Quote(EntityNaming.IdColumn)}";
            order = sort is null ? $"{id} ASC" : $"{OrderTerm(sort, joins, null)}, {id} ASC";
        }

        var (filter, filterValues) = dataSource.Filter is { } dataSourceFilter
            ? Translate(application, dataSource, dataSourceFilter, joins, "f")
            : (null, []);

        // The search joins its paths here, before the joins are read below.
        var searched = search
            ? $"({string.Join(" OR ", dataSource.SearchFields.Select(field => RecordQueries.SearchTerm(joins.ColumnOf(field.Path))))})"
            : null;
        string[] conditions = [.. new[] { filter, condition?.Sql, searched }.OfType<string>()];
        var where = conditions.Length == 0 ? "" : $" WHERE {string.Join(" AND ", conditions)}";
        var from = $"{table} AS {row}{joins.Sql}{where}";

        // Without a group field, the one total row has no order.
        var orderBy = order.Length == 0 ? "" : $" ORDER BY {order}";

        // A grouped count wraps the grouped statement, so it counts groups. Without a group field
        // the inner statement is one total row even when no row passes.
        var countSql = dataSource.Aggregate is null
            ? $"SELECT count(*) FROM {from}"
            : $"SELECT count(*) FROM (SELECT count(*) FROM {from}{groupBy}) AS g";
        var pageSql = $"SELECT {select} FROM {from}{groupBy}{orderBy} LIMIT @limit OFFSET @offset";
        var timeZoneId = dataSource.Aggregate is { } bucketed && bucketed.GroupBy.Any(field => field.Bucket is not null && field.Field.Type == FieldType.DateTime)
            ? application.TimeZone.Id
            : null;
        return new DataSourceStatement(countSql, pageSql, [.. filterValues, .. measureValues], from, timeZoneId);
    }

    /// <summary>
    /// <paramref name="filter"/>, the data source filter or a measure filter, as SQL and the values it
    /// names, which are called <paramref name="prefix"/> and a counter. A bare name in the filter is a
    /// parameter, matched ignoring letter case, or else a column of the root row. A longer path is a
    /// column of a joined target.
    /// </summary>
    private static (string Sql, IReadOnlyList<SqlValue> Values) Translate(
        ApplicationModel application, DataSourceModel dataSource, ExpressionModel filter, Joins joins, string prefix)
    {
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
            },
            filter.Check,
            parameterPrefix: prefix,
            timeZone: application.TimeZone);
        if (!translated.Succeeded)
        {
            throw new InvalidOperationException($"The compiler let through a filter outside the SQL subset: {translated.Diagnostic.Message}");
        }

        return (translated.Sql, translated.Parameters);
    }

    /// <summary>
    /// The entity id, the field name and the dotted path of every field the data source's filter reads,
    /// in the order the translation finds them, with the body of each rule it calls inlined. A bare
    /// name that is a parameter reads no field. Each step of a path through reference fields reads a
    /// field of the entity before it.
    /// </summary>
    internal static IReadOnlyList<(Guid EntityId, string Field, string Path)> FilterFieldsRead(
        ApplicationModel application, DataSourceModel dataSource) =>
        dataSource.Filter is { } filter ? FieldsRead(application, dataSource, filter) : [];

    /// <summary>
    /// The fields the filter of every measure reads, as <see cref="FilterFieldsRead"/> reports them for
    /// the data source filter, in measure order.
    /// </summary>
    internal static IReadOnlyList<(Guid EntityId, string Field, string Path)> MeasureFilterFieldsRead(
        ApplicationModel application, DataSourceModel dataSource) =>
        [.. dataSource.Aggregate?.Measures.SelectMany(measure =>
            measure.Filter is { } filter ? FieldsRead(application, dataSource, filter) : [])
            ?? []];

    private static List<(Guid EntityId, string Field, string Path)> FieldsRead(
        ApplicationModel application, DataSourceModel dataSource, ExpressionModel filter)
    {
        var read = new List<(Guid, string, string)>();
        var translated = SqlTranslator.Translate(
            filter.Syntax,
            names =>
            {
                if (names.Count == 1
                    && dataSource.Parameters.Any(parameter => string.Equals(parameter.Name, names[0], StringComparison.OrdinalIgnoreCase)))
                {
                    return "NULL";
                }

                var entityId = dataSource.Entity.Id;
                var path = string.Join('.', names);
                foreach (var field in ResolvePath(application, dataSource, names))
                {
                    read.Add((entityId, field.Name, path));
                    entityId = field.Target?.Id ?? Guid.Empty;
                }

                return "NULL";
            },
            filter.Check);
        if (!translated.Succeeded)
        {
            throw new InvalidOperationException($"The compiler let through a filter outside the SQL subset: {translated.Diagnostic.Message}");
        }

        return read;
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
    internal static void AddValues(NpgsqlCommand command, IReadOnlyList<SqlValue> values)
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

    /// <summary>Adds the time zone id of a statement that buckets a date-time as the text parameter <c>@tz</c>.</summary>
    private static void AddTimeZone(NpgsqlCommand command, DataSourceStatement statement)
    {
        if (statement.TimeZoneId is { } id)
        {
            command.Parameters.Add(new NpgsqlParameter("tz", NpgsqlDbType.Text) { Value = id });
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
                joins.ColumnOf(field.Path) + (FieldTypes.HoldsDecimal(field.Field.Type) ? "::text" : "")),
            .. References(dataSource).Select(field =>
                $"{joins.TargetAlias(field.Path)}.{EntityNaming.Quote(EntityNaming.Column(field.Field.TargetDisplayField!))}"),
        ]);

    private static IEnumerable<DataSourceFieldModel> References(DataSourceModel dataSource) =>
        References(dataSource.Fields);

    private static IEnumerable<DataSourceFieldModel> References(IEnumerable<DataSourceFieldModel> fields) =>
        fields.Where(field => field.Field.Type == FieldType.Reference);

    /// <summary>
    /// The sort column, or the aggregate of the sort measure with its filter, with its direction.
    /// <paramref name="measureFilters"/> is null without a grouping.
    /// </summary>
    private static string OrderTerm(DataSourceSort sort, Joins joins, IReadOnlyDictionary<string, string>? measureFilters) =>
        (sort.Measure is { } measure ? Aggregate(measure, joins, measureFilters!) : GroupColumn(sort.Field!, joins))
        + (sort.Descending ? " DESC" : " ASC");

    /// <summary>
    /// The select list of a grouped data source: every group field in <c>groupBy</c> order, then
    /// every measure, decimals and sums as text so no digit is lost. Then the display field of each
    /// group reference's target. Also the <c>GROUP BY</c> clause with a leading space: the group
    /// columns and those display columns, which each join matches at most once, so they never split
    /// a group. It is empty without a group field, which gives one total row.
    /// <paramref name="measureFilters"/> holds the SQL of each measure filter by measure name.
    /// </summary>
    private static (string Select, string GroupBy) GroupedSelectList(
        DataSourceAggregateModel aggregate, Joins joins, IReadOnlyDictionary<string, string> measureFilters)
    {
        var groupColumns = aggregate.GroupBy.Select(field => GroupColumn(field, joins)).ToList();
        var labelColumns = References(aggregate.GroupBy)
            .Select(field => $"{joins.TargetAlias(field.Path)}.{EntityNaming.Quote(EntityNaming.Column(field.Field.TargetDisplayField!))}")
            .ToList();
        var select = string.Join(", ", [
            .. aggregate.GroupBy.Select((field, index) =>
                groupColumns[index] + (FieldTypes.HoldsDecimal(field.Field.Type) ? "::text" : "")),
            .. aggregate.Measures.Select(measure => Aggregate(measure, joins, measureFilters) + (IsWrittenAsText(measure) ? "::text" : "")),
            .. labelColumns,
        ]);
        string[] grouping = [.. groupColumns, .. labelColumns];
        return (select, grouping.Length == 0 ? "" : $" GROUP BY {string.Join(", ", grouping)}");
    }

    /// <summary>
    /// The SQL of a group field or of a sort by it: its column, or for a bucketed field the first date of
    /// the bucket. A date-time is read in the application time zone, <c>@tz</c>, so each value falls in
    /// its local bucket. The unit comes from a fixed list, never from the file.
    /// </summary>
    private static string GroupColumn(DataSourceFieldModel field, Joins joins)
    {
        var column = joins.ColumnOf(field.Path);
        if (field.Bucket is not { } bucket)
        {
            return column;
        }

        var unit = bucket switch
        {
            DateBucket.Day => "day",
            DateBucket.Week => "week",
            DateBucket.Month => "month",
            DateBucket.Year => "year",
            _ => throw new ArgumentOutOfRangeException(nameof(field), bucket, "Unknown date bucket."),
        };
        var local = field.Field.Type == FieldType.DateTime ? $"{column} AT TIME ZONE @tz" : $"{column}::timestamp";
        return $"date_trunc('{unit}', {local})::date";
    }

    /// <summary>
    /// The aggregate of a measure, followed by <c>FILTER (WHERE ...)</c> when it has a filter. A sum,
    /// min, max or avg over only <c>NULL</c> values is <c>NULL</c>.
    /// </summary>
    private static string Aggregate(
        DataSourceMeasureModel measure, Joins joins, IReadOnlyDictionary<string, string> measureFilters)
    {
        var filter = measureFilters.TryGetValue(measure.Name, out var sql) ? $" FILTER (WHERE {sql})" : "";
        return measure.Function switch
        {
            AggregateFunction.Count => $"count(*){filter}",
            AggregateFunction.Sum => $"sum({joins.ColumnOf(measure.Field!.Path)}){filter}",
            AggregateFunction.Min => $"min({joins.ColumnOf(measure.Field!.Path)}){filter}",
            AggregateFunction.Max => $"max({joins.ColumnOf(measure.Field!.Path)}){filter}",
            AggregateFunction.Avg => $"round(avg({joins.ColumnOf(measure.Field!.Path)}){filter}, {AvgScale(measure.Field!.Field)})",
            AggregateFunction.CountDistinct => $"count(DISTINCT {joins.ColumnOf(measure.Field!.Path)}){filter}",
            _ => throw new ArgumentOutOfRangeException(nameof(measure), measure.Function, "Unknown aggregate function."),
        };
    }

    // A sum sent as text keeps its digits exactly, and so does a min or max of a decimal field.
    private static bool IsWrittenAsText(DataSourceMeasureModel measure) =>
        measure.Function is AggregateFunction.Sum or AggregateFunction.Avg
        || (measure.Function is AggregateFunction.Min or AggregateFunction.Max && FieldTypes.HoldsDecimal(measure.Field!.Field.Type));

    // An integer or an unbounded decimal keeps 6 digits. A decimal with a precision keeps 0 unless it has a scale.
    private static int AvgScale(FieldModel field) =>
        field.Type == FieldType.Integer ? 6 : field.Scale ?? (field.Precision is null ? 6 : 0);

    private static DataSourceRow ReadGroupedRow(NpgsqlDataReader reader, DataSourceAggregateModel aggregate)
    {
        var values = new Dictionary<string, JsonValue?>(aggregate.GroupBy.Count + aggregate.Measures.Count, StringComparer.Ordinal);
        var ordinal = 0;
        foreach (var field in aggregate.GroupBy)
        {
            values[field.Name] = reader.IsDBNull(ordinal) ? null : RecordQueries.ReadValue(reader, ordinal, field.Bucket is null ? field.Field : field.Field with { Type = FieldType.Date });
            ordinal++;
        }

        foreach (var measure in aggregate.Measures)
        {
            values[measure.Name] = reader.IsDBNull(ordinal)
                ? null
                : measure.Function switch
                {
                    AggregateFunction.Count or AggregateFunction.CountDistinct => JsonValue.Create(reader.GetInt64(ordinal)),
                    // The sum's and average's number text is written as is, like a decimal.
                    AggregateFunction.Sum or AggregateFunction.Avg => (JsonValue)JsonNode.Parse(reader.GetString(ordinal))!,
                    _ => RecordQueries.ReadValue(reader, ordinal, measure.Field!.Field),
                };
            ordinal++;
        }

        // A null reference, or a target whose display column is NULL, has no label.
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in References(aggregate.GroupBy))
        {
            if (!reader.IsDBNull(ordinal))
            {
                labels[field.Name] = reader.GetString(ordinal);
            }

            ordinal++;
        }

        return new DataSourceRow(null, values, labels);
    }

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

/// <summary>
/// The SQL of a data source read: the count statement, the page statement, the values of the
/// data source filter and then of each measure filter, which only the page statement names, and the
/// <c>FROM</c> clause with its joins and <c>WHERE</c> that both share, without the keyword.
/// <paramref name="TimeZoneId"/> is the id the count and page statements read as <c>@tz</c>, or null
/// when neither names it.
/// </summary>
internal sealed record DataSourceStatement(
    string CountSql, string PageSql, IReadOnlyList<SqlValue> Values, string From, string? TimeZoneId = null);
