using Npgsql;

namespace Axis.Data.Storage;

/// <summary>
/// A person's own rows in <c>axis.table_views</c> and <c>axis.table_columns</c>, see
/// <c>docs/reference/frontend.md#table-views-and-columns</c>. Every query names the user id and the table key, so a row of another
/// person or of another table is never read, changed or deleted. The database clock is the only clock used.
/// </summary>
public static class TableViews
{
    /// <summary>The most views a person keeps for one table.</summary>
    public const int MaxViews = 50;

    /// <summary>The name of the unique index on a person's, a table's and a lower-case view name.</summary>
    private const string NameIndex = "ux_table_views_user_id_table_key_name";

    private const string ReadColumnsSql =
        """
        SELECT "columns" FROM "axis"."table_columns" WHERE "user_id" = @user AND "table_key" = @key
        """;

    private const string ListViewsSql =
        """
        SELECT "id", "name", "search", "filter", "sort" FROM "axis"."table_views"
        WHERE "user_id" = @user AND "table_key" = @key ORDER BY lower("name"), "id"
        """;

    private const string SetColumnsSql =
        """
        INSERT INTO "axis"."table_columns" ("user_id", "table_key", "columns") VALUES (@user, @key, @columns)
        ON CONFLICT ("user_id", "table_key") DO UPDATE SET "columns" = excluded."columns", "updated_at" = now()
        """;

    private const string ResetColumnsSql =
        """
        DELETE FROM "axis"."table_columns" WHERE "user_id" = @user AND "table_key" = @key
        """;

    // Two saves at once cannot both pass the count, because the lock is held until the transaction ends.
    private const string LockSql =
        """
        SELECT pg_advisory_xact_lock(hashtextextended('axis-table-views:' || @user || ':' || @key, 0))
        """;

    private const string CountSql =
        """
        SELECT count(*) FROM "axis"."table_views" WHERE "user_id" = @user AND "table_key" = @key
        """;

    private const string InsertSql =
        """
        INSERT INTO "axis"."table_views" ("id", "user_id", "table_key", "name", "search", "filter", "sort")
        VALUES (@id, @user, @key, @name, @search, @filter, @sort)
        """;

    private const string ReplaceSql =
        """
        UPDATE "axis"."table_views"
        SET "name" = @name, "search" = @search, "filter" = @filter, "sort" = @sort, "updated_at" = now()
        WHERE "id" = @id AND "user_id" = @user AND "table_key" = @key
        """;

    private const string DeleteSql =
        """
        DELETE FROM "axis"."table_views" WHERE "id" = @id AND "user_id" = @user AND "table_key" = @key
        """;

    /// <summary>One saved view of a table.</summary>
    public sealed record View(Guid Id, string Name, string? Search, string? Filter, string? Sort);

    /// <summary>What a write of a view did.</summary>
    public enum WriteResult
    {
        /// <summary>The view was saved, replaced or deleted.</summary>
        Done,

        /// <summary>The person has no view with this id for this table.</summary>
        NotFound,

        /// <summary>Another view of the person's table has the name, ignoring letter case.</summary>
        NameTaken,

        /// <summary>The person already has <see cref="MaxViews"/> views for the table.</summary>
        TooMany,
    }

    /// <summary>Reads the kept columns, or <c>null</c> when none are kept, and the views ordered by name.</summary>
    public static async Task<(IReadOnlyList<string>? Columns, IReadOnlyList<View> Views)> ReadAsync(
        NpgsqlConnection connection, string userId, string tableKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        IReadOnlyList<string>? columns = null;
        await using (var command = Command(ReadColumnsSql, connection, null, userId, tableKey))
        {
            if (await command.ExecuteScalarAsync(cancellationToken) is string[] kept)
            {
                columns = kept;
            }
        }

        var views = new List<View>();
        await using (var command = Command(ListViewsSql, connection, null, userId, tableKey))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                views.Add(new View(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetString(2),
                    await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetString(3),
                    await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetString(4)));
            }
        }

        return (columns, views);
    }

    /// <summary>Keeps the columns of the person's table, replacing any kept before.</summary>
    public static async Task SetColumnsAsync(
        NpgsqlConnection connection, string userId, string tableKey, IReadOnlyList<string> columns, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(columns);

        await using var command = Command(SetColumnsSql, connection, null, userId, tableKey);
        command.Parameters.AddWithValue("columns", columns.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Forgets the kept columns of the person's table. Forgetting what is not kept is not an error.</summary>
    public static async Task ResetColumnsAsync(NpgsqlConnection connection, string userId, string tableKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = Command(ResetColumnsSql, connection, null, userId, tableKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Saves a new view in a transaction of its own. It takes a lock for the person and the table before it counts, so two saves at once
    /// cannot pass <see cref="MaxViews"/>. It returns <see cref="WriteResult.TooMany"/> or <see cref="WriteResult.NameTaken"/> and saves nothing
    /// when the limit or the unique name stops it.
    /// </summary>
    public static async Task<WriteResult> SaveAsync(
        NpgsqlConnection connection, string userId, string tableKey, View view, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(view);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var @lock = Command(LockSql, connection, transaction, userId, tableKey))
        {
            await @lock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var count = Command(CountSql, connection, transaction, userId, tableKey))
        {
            if ((long)(await count.ExecuteScalarAsync(cancellationToken))! >= MaxViews)
            {
                return WriteResult.TooMany;
            }
        }

        try
        {
            await using var insert = Command(InsertSql, connection, transaction, userId, tableKey);
            AddView(insert, view);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (IsNameTaken(exception))
        {
            return WriteResult.NameTaken;
        }

        await transaction.CommitAsync(cancellationToken);
        return WriteResult.Done;
    }

    /// <summary>Replaces the name and the three fields of the person's view with this id for this table.</summary>
    public static async Task<WriteResult> ReplaceAsync(
        NpgsqlConnection connection, string userId, string tableKey, View view, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(view);

        try
        {
            await using var command = Command(ReplaceSql, connection, null, userId, tableKey);
            AddView(command, view);
            return await command.ExecuteNonQueryAsync(cancellationToken) > 0 ? WriteResult.Done : WriteResult.NotFound;
        }
        catch (PostgresException exception) when (IsNameTaken(exception))
        {
            return WriteResult.NameTaken;
        }
    }

    /// <summary>Deletes the person's view with this id for this table. It returns <see cref="WriteResult.NotFound"/> when there is none.</summary>
    public static async Task<WriteResult> DeleteAsync(
        NpgsqlConnection connection, string userId, string tableKey, Guid id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = Command(DeleteSql, connection, null, userId, tableKey);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0 ? WriteResult.Done : WriteResult.NotFound;
    }

    private static bool IsNameTaken(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == NameIndex;

    private static NpgsqlCommand Command(string sql, NpgsqlConnection connection, NpgsqlTransaction? transaction, string userId, string tableKey)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("key", tableKey);
        return command;
    }

    private static void AddView(NpgsqlCommand command, View view)
    {
        command.Parameters.AddWithValue("id", view.Id);
        command.Parameters.AddWithValue("name", view.Name);
        command.Parameters.Add(new NpgsqlParameter("search", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)view.Search ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("filter", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)view.Filter ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("sort", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)view.Sort ?? DBNull.Value });
    }
}
