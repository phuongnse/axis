using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Expressions.Sql;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Records;

/// <summary>
/// Reads records of an entity table over a tenant connection. Every identifier comes from
/// <see cref="EntityNaming"/> and is quoted, and every value from the caller is a parameter.
/// </summary>
public static class RecordQueries
{
    private const string DateFormat = "yyyy-MM-dd";

    private const string CountSavepoint = "count";

    // PostgreSQL stores microseconds, so six fraction digits show every stored value exactly.
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    /// <summary>The alias of the entity table in a read. Joined targets have their own id and version columns.</summary>
    internal const string RowAlias = "r";

    /// <summary>
    /// Reads page <paramref name="page"/> of <paramref name="pageSize"/> records, ordered by the
    /// sort field and then by id ascending, or by id alone without a sort, and counts the records
    /// that match. A page past the last one has no items. A <paramref name="search"/>
    /// with fields keeps only the records where any searched field contains its text, ignoring case,
    /// with no wildcard characters; the text is a parameter. A
    /// <paramref name="condition"/>, such as the user's read filters, keeps only the records that
    /// match it, in both the page and the count. Returns <see langword="null"/> when the database
    /// rejects the condition for these records with a data exception (SQLSTATE class 22), such as
    /// an integer overflow. Any other database error is thrown.
    /// When <paramref name="transaction"/>, the transaction of <paramref name="connection"/>, is given,
    /// the count runs under a savepoint. A count that the database cancels at the statement timeout
    /// (SQLSTATE 57014) then leaves <see cref="RecordPage.TotalCount"/> null, and the page is still
    /// read. Without a transaction that error is thrown.
    /// </summary>
    public static async Task<RecordPage?> ListAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        EntityModel entity,
        int page,
        int pageSize,
        RecordSort? sort,
        RecordSearch? search,
        RecordCondition? condition = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var statement = ListStatement(entity, sort, search, condition);
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
                search?.AddTo(count);

                condition?.AddTo(count);
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
            search?.AddTo(command);

            condition?.AddTo(command);
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
        catch (PostgresException exception) when (condition is not null && exception.SqlState.StartsWith("22", StringComparison.Ordinal))
        {
            // Class 22 is a data exception: the condition could not be evaluated for some record.
            return null;
        }
    }

    /// <summary>
    /// Streams up to <paramref name="limit"/> records in the order and under the search and condition of
    /// <see cref="ListAsync"/>, one at a time, so a caller never holds them all. It does not count. A database
    /// error is thrown as it is, including a data exception (SQLSTATE class 22) that the
    /// <paramref name="condition"/> raises for some record, which the caller maps.
    /// </summary>
    public static async IAsyncEnumerable<Record> ReadAllAsync(
        NpgsqlConnection connection,
        EntityModel entity,
        RecordSort? sort,
        RecordSearch? search,
        RecordCondition? condition,
        int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var statement = ListStatement(entity, sort, search, condition);
        await using var command = new NpgsqlCommand(statement.PageSql, connection);
        search?.AddTo(command);

        condition?.AddTo(command);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("offset", 0L);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return ReadRecord(reader, entity);
        }
    }

    /// <summary>
    /// The count and the page statement of a list of <paramref name="entity"/>. The page statement
    /// ends with <c>LIMIT @limit OFFSET @offset</c>. When <paramref name="search"/> has fields, both
    /// keep only the records where any searched field contains <c>@search</c>. Both keep only the
    /// records that match <paramref name="condition"/>.
    /// Neither holds a value: each is a parameter that the caller adds.
    /// </summary>
    internal static RecordListStatement ListStatement(EntityModel entity, RecordSort? sort, RecordSearch? search, RecordCondition? condition)
    {
        var table = Table(entity);
        var row = EntityNaming.Quote(RowAlias);
        var conditions = new List<string>();
        if (search is { Fields.Count: > 0 })
        {
            conditions.Add(search.Sql(RowAlias));
        }

        if (condition is not null)
        {
            conditions.Add(condition.Sql);
        }

        var where = conditions.Count > 0 ? $" WHERE {string.Join(" AND ", conditions)}" : "";
        var id = $"{row}.{EntityNaming.Quote(EntityNaming.IdColumn)}";
        var order = sort is null
            ? $"{id} ASC"
            : $"{row}.{EntityNaming.Quote(EntityNaming.Column(sort.Field.Name))} {(sort.Descending ? "DESC" : "ASC")}, {id} ASC";
        return new RecordListStatement(
            $"SELECT count(*) FROM {table} AS {row}{where}",
            $"SELECT {SelectList(entity, RowAlias)} FROM {table} AS {row}{LabelJoins(entity, RowAlias)}{where} ORDER BY {order} LIMIT @limit OFFSET @offset");
    }

    /// <summary>
    /// The condition that <paramref name="column"/> contains <c>@search</c>, ignoring letter case.
    /// It is a <c>LIKE</c> over <c>lower(column)</c>, so the trigram index of the column serves it.
    /// </summary>
    internal static string SearchTerm(string column) => $"lower({column}) LIKE lower(@search)";

    /// <summary>The <c>@search</c> parameter for <paramref name="search"/>, as a pattern that matches it as plain text.</summary>
    internal static NpgsqlParameter SearchParameter(string search) =>
        new("search", NpgsqlDbType.Text) { Value = SearchPattern(search) };

    // The backslash is the default LIKE escape character, and it is escaped first.
    internal static string SearchPattern(string text) =>
        "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";

    /// <summary>
    /// Reads the start time of the current transaction, which is what <c>now()</c> gives in an
    /// expression. Call it inside the caller's transaction, so it is that transaction's time.
    /// A test scenario fixes it with the session setting <c>axis.now</c>, which wins when it is set.
    /// </summary>
    public static async Task<DateTimeOffset> TransactionTimeAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand($"SELECT {SqlTranslator.NowSql}", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return reader.GetFieldValue<DateTimeOffset>(0);
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

        var record = await FindRowAsync(connection, entity, id, cancellationToken);
        return record is null ? null : await WithRowsAsync(connection, application, entity, record, cancellationToken);
    }

    /// <summary>
    /// Reads the display field value of each record of <paramref name="ids"/>, by record id, in
    /// one command. A record that is missing or whose display value is NULL has no entry, and an
    /// entity without a display field gives no entries.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> ReadDisplayLabelsAsync(
        NpgsqlConnection connection,
        EntityModel entity,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(ids);

        var labels = new Dictionary<Guid, string>();
        if (entity.DisplayField is null || ids.Count == 0)
        {
            return labels;
        }

        var id = EntityNaming.Quote(EntityNaming.IdColumn);
        var display = EntityNaming.Quote(EntityNaming.Column(entity.DisplayField));
        await using var command = new NpgsqlCommand(
            $"SELECT {id}, {display} FROM {Table(entity)} WHERE {id} = ANY(@ids) AND {display} IS NOT NULL",
            connection);
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids.ToArray() });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            labels[reader.GetGuid(0)] = reader.GetString(1);
        }

        return labels;
    }

    /// <summary>
    /// Finds the ids of the records of <paramref name="entity"/> whose display field equals <paramref name="value"/>
    /// exactly, at most two, so a caller can tell none, one and more than one apart. The records' filters are not
    /// applied. An entity without a display field gives none. The command joins the transaction that
    /// <paramref name="connection"/> has open.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> FindByDisplayValueAsync(
        NpgsqlConnection connection,
        EntityModel entity,
        string value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(value);

        var ids = new List<Guid>();
        if (entity.DisplayField is null)
        {
            return ids;
        }

        var id = EntityNaming.Quote(EntityNaming.IdColumn);
        var display = EntityNaming.Quote(EntityNaming.Column(entity.DisplayField));
        await using var command = new NpgsqlCommand($"SELECT {id} FROM {Table(entity)} WHERE {display} = @value LIMIT 2", connection);
        command.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Text) { Value = value });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    /// <summary>
    /// Reads the row of the record with <paramref name="id"/>, with its labels but without the rows
    /// of its child collections, in one command. <see langword="null"/> when there is none.
    /// </summary>
    internal static async Task<Record?> FindRowAsync(
        NpgsqlConnection connection, EntityModel entity, Guid id, CancellationToken cancellationToken)
    {
        var row = EntityNaming.Quote(RowAlias);
        await using var command = new NpgsqlCommand(
            $"SELECT {SelectList(entity, RowAlias)} FROM {Table(entity)} AS {row}{LabelJoins(entity, RowAlias)} WHERE {row}.{EntityNaming.Quote(EntityNaming.IdColumn)} = @id",
            connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader, entity) : null;
    }

    /// <summary>
    /// Adds the rows of each child collection of <paramref name="entity"/> to
    /// <paramref name="record"/>, in position order, with one query per collection. The values
    /// keep the entity's field declaration order. A collection whose child entity has reference
    /// fields also adds the row labels, read in the same query, under the collection's name.
    /// A references field gets its ids in written order as the value, and the label of each id at
    /// the same position as its labels, with <see langword="null"/> for a target without a display value.
    /// A file field is read as <c>{ id, name, mediaType, size }</c>, in the row too.
    /// </summary>
    internal static async Task<Record> WithRowsAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        Record record,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, JsonNode?>(entity.Fields.Count, StringComparer.Ordinal);
        var labels = new Dictionary<string, JsonNode>(record.Labels, StringComparer.Ordinal);
        foreach (var field in entity.Fields)
        {
            if (field.Type == FieldType.References)
            {
                var (ids, setLabels) = await ReadLinksAsync(connection, entity, field, record.Id, cancellationToken);
                values[field.Name] = ids;
                labels[field.Name] = setLabels;
            }
            else if (field.Type == FieldType.ChildCollection)
            {
                var (rows, rowLabels) = await ReadRowsAsync(connection, application, field, record.Id, cancellationToken);
                values[field.Name] = rows;
                if (rowLabels is not null)
                {
                    labels[field.Name] = rowLabels;
                }
            }
            else
            {
                values[field.Name] = record.Values[field.Name];
            }
        }

        return record with { Values = values, Labels = labels };
    }

    /// <summary>
    /// The ids of a references field in the order they were written, and the display value of each
    /// target at the same position. A target whose display value is NULL, or an entity without a
    /// display field, gives <see langword="null"/>.
    /// </summary>
    private static async Task<(JsonArray Ids, JsonArray Labels)> ReadLinksAsync(
        NpgsqlConnection connection,
        EntityModel entity,
        FieldModel field,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var junction = EntityNaming.Quote("j");
        var target = EntityNaming.Quote("t");
        var display = field.TargetDisplayField is null
            ? "NULL"
            : $"{target}.{EntityNaming.Quote(EntityNaming.Column(field.TargetDisplayField))}";
        await using var command = new NpgsqlCommand(
            $"SELECT {junction}.{EntityNaming.Quote(EntityNaming.TargetColumn)}, {display} "
            + $"FROM {EntityNaming.QualifiedTable(EntityNaming.Junction(entity.Id, field.Name))} AS {junction} "
            + $"JOIN {EntityNaming.QualifiedTable(EntityNaming.Table(field.Target!.Id))} AS {target} "
            + $"ON {target}.{EntityNaming.Quote(EntityNaming.IdColumn)} = {junction}.{EntityNaming.Quote(EntityNaming.TargetColumn)} "
            + $"WHERE {junction}.{EntityNaming.Quote(EntityNaming.OwnerColumn)} = @owner "
            + $"ORDER BY {junction}.{EntityNaming.Quote(EntityNaming.PositionColumn)}",
            connection);
        command.Parameters.AddWithValue("owner", ownerId);

        var ids = new JsonArray();
        var labels = new JsonArray();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0).ToString("D"));
            labels.Add(reader.IsDBNull(1) ? null : reader.GetString(1));
        }

        return (ids, labels);
    }

    /// <summary>
    /// The rows of a child collection, and one object of labels per row when the child entity has
    /// reference fields, otherwise <see langword="null"/>. A reference without a label is left out of its row's object.
    /// </summary>
    private static async Task<(JsonArray Rows, JsonArray? Labels)> ReadRowsAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        FieldModel collection,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var child = application.FindEntity(collection.Target!.Id)
            ?? throw new InvalidOperationException("The application has no entity for the child collection.");
        var columns = Columns(child);
        var references = References(child).ToList();
        var files = Files(child).ToList();
        var alias = EntityNaming.Quote(RowAlias);
        var selected = columns.Select(field =>
            alias + "." + EntityNaming.Quote(EntityNaming.Column(field.Name)) + (FieldTypes.HoldsDecimal(field.Type) ? "::text" : ""))
            .Concat(references.Select((field, index) =>
                $"{EntityNaming.Quote(LabelAlias(index))}.{EntityNaming.Quote(EntityNaming.Column(field.TargetDisplayField!))}"))
            .Concat(files.Select((_, index) => FileColumns(index)));
        await using var command = new NpgsqlCommand(
            $"SELECT {string.Join(", ", selected)} FROM {Table(child)} AS {alias}{LabelJoins(child, RowAlias)} WHERE {alias}.{EntityNaming.Quote(EntityNaming.OwnerColumn)} = @owner ORDER BY {alias}.{EntityNaming.Quote(EntityNaming.PositionColumn)}",
            connection);
        command.Parameters.AddWithValue("owner", ownerId);

        var rows = new JsonArray();
        var labels = references.Count > 0 ? new JsonArray() : null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new JsonObject();
            for (var ordinal = 0; ordinal < columns.Count; ordinal++)
            {
                row[columns[ordinal].Name] = reader.IsDBNull(ordinal) ? null : ReadValue(reader, ordinal, columns[ordinal]);
            }

            ReadFiles(reader, columns.Count + references.Count, files, row);
            rows.Add(row);

            if (labels is not null)
            {
                var rowLabels = new JsonObject();
                for (var index = 0; index < references.Count; index++)
                {
                    var ordinal = columns.Count + index;
                    if (!reader.IsDBNull(ordinal))
                    {
                        rowLabels[references[index].Name] = reader.GetString(ordinal);
                    }
                }

                labels.Add(rowLabels);
            }
        }

        return (rows, labels);
    }

    internal static string Table(EntityModel entity) => EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id));

    /// <summary>
    /// The id, the version, then every column field in declaration order, all of the row named
    /// <paramref name="alias"/>; decimals as text so no digit is lost. Then the display field of
    /// each reference's target, then the name, media type and size of each file field's file, both
    /// joined by <see cref="LabelJoins"/>.
    /// </summary>
    internal static string SelectList(EntityModel entity, string alias)
    {
        var row = EntityNaming.Quote(alias) + ".";
        return string.Join(", ", [
            row + EntityNaming.Quote(EntityNaming.IdColumn),
            row + EntityNaming.Quote(EntityNaming.VersionColumn),
            .. Columns(entity).Select(field =>
                row + EntityNaming.Quote(EntityNaming.Column(field.Name)) + (FieldTypes.HoldsDecimal(field.Type) ? "::text" : "")),
            .. References(entity).Select((field, index) =>
                $"{EntityNaming.Quote(LabelAlias(index))}.{EntityNaming.Quote(EntityNaming.Column(field.TargetDisplayField!))}"),
            .. Files(entity).Select((_, index) => FileColumns(index)),
        ]);
    }

    private static string FileColumns(int index)
    {
        var file = EntityNaming.Quote(FileAlias(index));
        return $"{file}.\"name\", {file}.\"media_type\", {file}.\"size\"";
    }

    /// <summary>
    /// One left join per reference field to the target table, aliased by the reference's position,
    /// so each record's labels come from the same statement. Then one left join per file field to
    /// <c>axis.files</c>, aliased by the file field's position. Empty without a reference or file field.
    /// </summary>
    internal static string LabelJoins(EntityModel entity, string alias) =>
        string.Concat(References(entity).Select((field, index) =>
        {
            var target = EntityNaming.Quote(LabelAlias(index));
            return $" LEFT JOIN {EntityNaming.QualifiedTable(EntityNaming.Table(field.Target!.Id))} AS {target}"
                + $" ON {target}.{EntityNaming.Quote(EntityNaming.IdColumn)} = {EntityNaming.Quote(alias)}.{EntityNaming.Quote(EntityNaming.Column(field.Name))}";
        }).Concat(Files(entity).Select((field, index) =>
        {
            var file = EntityNaming.Quote(FileAlias(index));
            return $" LEFT JOIN \"axis\".\"files\" AS {file}"
                + $" ON {file}.\"id\" = {EntityNaming.Quote(alias)}.{EntityNaming.Quote(EntityNaming.Column(field.Name))}";
        })));

    private static string LabelAlias(int index) => $"l{index}";

    private static string FileAlias(int index) => $"fl{index}";

    private static IEnumerable<FieldModel> References(EntityModel entity) =>
        Columns(entity).Where(field => field.Type == FieldType.Reference);

    private static IEnumerable<FieldModel> Files(EntityModel entity) =>
        Columns(entity).Where(field => field.Type == FieldType.File);

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
        var labels = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        var labelOrdinal = columns.Count + 2;
        foreach (var field in References(entity))
        {
            if (!reader.IsDBNull(labelOrdinal))
            {
                labels[field.Name] = JsonValue.Create(reader.GetString(labelOrdinal))!;
            }

            labelOrdinal++;
        }

        ReadFiles(reader, labelOrdinal, Files(entity).ToList(), values);
        return new Record(reader.GetGuid(0), reader.GetInt64(1), values, labels);
    }

    /// <summary>
    /// Replaces the id of each file field in <paramref name="values"/> by <c>{ id, name, mediaType, size }</c>,
    /// read from three columns each from <paramref name="ordinal"/>. A null file id stays null.
    /// </summary>
    private static void ReadFiles(NpgsqlDataReader reader, int ordinal, IReadOnlyList<FieldModel> files, IDictionary<string, JsonNode?> values)
    {
        foreach (var field in files)
        {
            if (values[field.Name] is { } id)
            {
                values[field.Name] = new JsonObject
                {
                    ["id"] = id.GetValue<string>(),
                    ["name"] = reader.GetString(ordinal),
                    ["mediaType"] = reader.GetString(ordinal + 1),
                    ["size"] = reader.GetInt64(ordinal + 2),
                };
            }

            ordinal += 3;
        }
    }

    internal static JsonValue ReadValue(NpgsqlDataReader reader, int ordinal, FieldModel field) =>
        field.Type switch
        {
            FieldType.Text or FieldType.Enum => JsonValue.Create(reader.GetString(ordinal)),
            FieldType.Integer => JsonValue.Create(reader.GetInt64(ordinal)),
            // The number text is written as is, so trailing zeros and every digit are kept.
            FieldType.Decimal or FieldType.Money => (JsonValue)JsonNode.Parse(reader.GetString(ordinal))!,
            FieldType.Boolean => JsonValue.Create(reader.GetBoolean(ordinal)),
            FieldType.Date => JsonValue.Create(reader.GetFieldValue<DateOnly>(ordinal).ToString(DateFormat, CultureInfo.InvariantCulture)),
            // Npgsql reads timestamp with time zone as a UTC DateTime.
            FieldType.DateTime => JsonValue.Create(reader.GetFieldValue<DateTime>(ordinal).ToString(DateTimeFormat, CultureInfo.InvariantCulture)),
            FieldType.Reference or FieldType.File => JsonValue.Create(reader.GetGuid(ordinal).ToString("D")),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field.Type, "Unknown field type."),
        };
}

/// <summary>The count and the page statement of a record list, as <see cref="RecordQueries.ListStatement"/> builds them.</summary>
internal sealed record RecordListStatement(string CountSql, string PageSql);
