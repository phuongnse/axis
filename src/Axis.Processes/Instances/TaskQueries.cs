using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Processes.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Reads the human tasks of process instances over a connection to a tenant database, and decides
/// who may act on one. A user may act on a task assigned to their id, or to a role they hold. A role
/// task that a holder claimed is listed and completed only by its claimer. Ids and role names match
/// exactly, including letter case.
/// </summary>
public static class TaskQueries
{
    private const string Columns =
        """
        id, process_instance_id, process_id, release_id, application_id, step, subject_entity_id, subject_id,
        assignee_kind, assignee, form_id, due_at, state, outcome, completed_by, completed_at, created_at, expired_at, claimed_by, claimed_at
        """;

    private const string MayActWhere =
        $"""
        application_id = @application AND state = '{ProcessTasks.Open}'
          AND ((assignee_kind = '{ProcessTasks.UserAssignee}' AND assignee = @user)
            OR (assignee_kind = '{ProcessTasks.RoleAssignee}' AND assignee = ANY(@roles)
                AND (claimed_by IS NULL OR claimed_by = @user)))
        """;

    /// <summary>Counts the open tasks of an application that a user may act on.</summary>
    internal const string ListOpenCountSql = $"SELECT count(*) FROM axis.process_tasks WHERE {MayActWhere}";

    /// <summary>Reads one page of the open tasks of an application that a user may act on.</summary>
    internal const string ListOpenPageSql =
        $"""
        SELECT {Columns} FROM axis.process_tasks WHERE {MayActWhere}
        ORDER BY due_at ASC NULLS LAST, created_at, id
        LIMIT @limit OFFSET @offset
        """;

    /// <summary>
    /// Reads page <paramref name="page"/> of <paramref name="pageSize"/> open tasks of the
    /// application that <paramref name="userId"/> may act on, whatever release their instance is
    /// pinned to, and counts them. Tasks are ordered by due date with no due date last, then by
    /// creation time, then by id. A page past the last one has no items.
    /// </summary>
    public static Task<TaskPage> ListOpenAsync(
        NpgsqlConnection connection,
        Guid applicationId,
        string userId,
        IReadOnlyCollection<string> roles,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        ListOpenAsync(connection, applicationId, userId, roles, new ListSql("TRUE", null, null, null), page, pageSize, cancellationToken);

    /// <summary>
    /// Reads page <paramref name="page"/> of <paramref name="pageSize"/> open tasks like the overload without a list, and counts
    /// only those that match <paramref name="list"/>. A sorted list orders by its sort first and breaks ties in the default
    /// order: due date with no due date last, then creation time, then id. A list with no condition and no sort runs
    /// <see cref="ListOpenCountSql"/> and <see cref="ListOpenPageSql"/> as they are.
    /// </summary>
    public static async Task<TaskPage> ListOpenAsync(
        NpgsqlConnection connection,
        Guid applicationId,
        string userId,
        IReadOnlyCollection<string> roles,
        ListSql list,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var plain = list.Condition == "TRUE" && list.Order is null;
        var countSql = plain ? ListOpenCountSql : $"{ListOpenCountSql} AND {list.Condition}";
        var pageSql = plain
            ? ListOpenPageSql
            : $"""
              SELECT {Columns} FROM axis.process_tasks WHERE {MayActWhere} AND {list.Condition}
              ORDER BY {(list.Order is null ? "" : $"{list.Order}, ")}due_at ASC NULLS LAST, created_at, id
              LIMIT @limit OFFSET @offset
              """;

        long totalCount;
        await using (var count = new NpgsqlCommand(countSql, connection))
        {
            AddMayActParameters(count, applicationId, userId, roles);
            list.AddTo(count);
            totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

        await using var command = new NpgsqlCommand(pageSql, connection);
        AddMayActParameters(command, applicationId, userId, roles);
        list.AddTo(command);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (long)(page - 1) * pageSize);

        var items = new List<ProcessTaskRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadTask(reader));
        }

        return new TaskPage(items, totalCount);
    }

    /// <summary>
    /// Reads the distinct pairs of release and subject entity of the open tasks of the application that
    /// <paramref name="userId"/> may act on, which are the pairs that <see cref="SubjectLabelSql"/> builds its label over.
    /// </summary>
    public static async Task<IReadOnlyList<(Guid ReleaseId, Guid SubjectEntityId)>> ListOpenSubjectsAsync(
        NpgsqlConnection connection,
        Guid applicationId,
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(roles);

        await using var command = new NpgsqlCommand(
            $"SELECT DISTINCT release_id, subject_entity_id FROM axis.process_tasks WHERE {MayActWhere}", connection);
        AddMayActParameters(command, applicationId, userId, roles);
        var pairs = new List<(Guid, Guid)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pairs.Add((reader.GetGuid(0), reader.GetGuid(1)));
        }

        return pairs;
    }

    /// <summary>
    /// The SQL expression of the subject label of a task in <c>axis.process_tasks</c>, for a list's filter, search and sort.
    /// <paramref name="labelled"/> holds the (release, entity) pairs whose label the reader may see. The label of a task in
    /// any other pair is NULL, so a hidden label is never matched, compared or sorted by its value. Throws when an
    /// entity has no display field. The ids come from the database, never from a request.
    /// </summary>
    public static string SubjectLabelSql(IReadOnlyList<(Guid ReleaseId, EntityModel Entity)> labelled)
    {
        ArgumentNullException.ThrowIfNull(labelled);

        if (labelled.Count == 0)
        {
            return "NULL::text";
        }

        var branches = labelled.Select(pair =>
        {
            var displayField = pair.Entity.DisplayField
                ?? throw new InvalidOperationException($"The entity '{pair.Entity.Name}' has no display field.");
            return $"WHEN process_tasks.release_id = '{pair.ReleaseId:D}'::uuid AND process_tasks.subject_entity_id = '{pair.Entity.Id:D}'::uuid "
                + $"THEN (SELECT ls.{EntityNaming.Quote(EntityNaming.Column(displayField))} FROM {RecordQueries.Table(pair.Entity)} AS ls "
                + $"WHERE ls.{EntityNaming.Quote(EntityNaming.IdColumn)} = process_tasks.subject_id)";
        });
        return $"CASE {string.Join(" ", branches)} END";
    }

    /// <summary>Reads the task with <paramref name="id"/> in any state, or <see langword="null"/> when there is none.</summary>
    public static async Task<ProcessTaskRow?> FindAsync(NpgsqlConnection connection, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand($"SELECT {Columns} FROM axis.process_tasks WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTask(reader) : null;
    }

    /// <summary>Reads the tasks of the instance <paramref name="instanceId"/> in any state, oldest first.</summary>
    public static async Task<IReadOnlyList<ProcessTaskRow>> ListByInstanceAsync(
        NpgsqlConnection connection,
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand(
            $"SELECT {Columns} FROM axis.process_tasks WHERE process_instance_id = @id ORDER BY created_at, id",
            connection);
        command.Parameters.AddWithValue("id", instanceId);
        var items = new List<ProcessTaskRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadTask(reader));
        }

        return items;
    }

    /// <summary>
    /// Reads and locks the task with <paramref name="id"/> in any state until the transaction ends,
    /// or returns <see langword="null"/> when there is none. A concurrent lock waits for this
    /// transaction, then reads the task as it committed.
    /// </summary>
    public static async Task<ProcessTaskRow?> LockAsync(NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = new NpgsqlCommand(
            $"SELECT {Columns} FROM axis.process_tasks WHERE id = @id FOR UPDATE",
            transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction)),
            transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTask(reader) : null;
    }

    /// <summary>
    /// Whether <paramref name="userId"/>, holding <paramref name="roles"/>, may act on
    /// <paramref name="task"/>: they are its assignee, or its assignee is a role they hold.
    /// </summary>
    public static bool MayAct(ProcessTaskRow task, string userId, IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(roles);

        return task.AssigneeKind switch
        {
            ProcessTasks.UserAssignee => string.Equals(task.Assignee, userId, StringComparison.Ordinal),
            ProcessTasks.RoleAssignee => roles.Contains(task.Assignee, StringComparer.Ordinal),
            _ => false,
        };
    }

    /// <summary>
    /// Whether <paramref name="userId"/> may complete <paramref name="task"/>: they may act on it
    /// and no one else holds its claim.
    /// </summary>
    public static bool MayComplete(ProcessTaskRow task, string userId, IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(task);

        return MayAct(task, userId, roles)
            && (task.ClaimedBy is null || string.Equals(task.ClaimedBy, userId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether <paramref name="task"/> is overdue at <paramref name="now"/>: it is open and its due
    /// date is before <paramref name="now"/>. A task in any other state is never overdue.
    /// </summary>
    public static bool IsOverdue(ProcessTaskRow task, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(task);

        return task.State == ProcessTasks.Open && task.DueAt is { } due && due < now;
    }

    internal static void AddMayActParameters(NpgsqlCommand command, Guid applicationId, string userId, IReadOnlyCollection<string> roles)
    {
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.Add(new NpgsqlParameter("user", NpgsqlDbType.Text) { Value = userId });
        command.Parameters.Add(new NpgsqlParameter("roles", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = roles.ToArray() });
    }

    private static ProcessTaskRow ReadTask(NpgsqlDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            ProcessInstanceId = reader.GetGuid(1),
            ProcessId = reader.GetGuid(2),
            ReleaseId = reader.GetGuid(3),
            ApplicationId = reader.GetGuid(4),
            Step = reader.GetString(5),
            SubjectEntityId = reader.GetGuid(6),
            SubjectId = reader.GetGuid(7),
            AssigneeKind = reader.GetString(8),
            Assignee = reader.GetString(9),
            FormId = reader.GetGuid(10),
            DueAt = reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
            State = reader.GetString(12),
            Outcome = reader.IsDBNull(13) ? null : reader.GetString(13),
            CompletedBy = reader.IsDBNull(14) ? null : reader.GetString(14),
            CompletedAt = reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(16),
            ExpiredAt = reader.IsDBNull(17) ? null : reader.GetFieldValue<DateTimeOffset>(17),
            ClaimedBy = reader.IsDBNull(18) ? null : reader.GetString(18),
            ClaimedAt = reader.IsDBNull(19) ? null : reader.GetFieldValue<DateTimeOffset>(19),
        };
}

/// <summary>One page of the tasks a user may act on, and the number of them on every page.</summary>
public sealed record TaskPage(IReadOnlyList<ProcessTaskRow> Items, long TotalCount);
