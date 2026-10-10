using Axis.Processes.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Reads the human tasks of process instances over a connection to a tenant database, and decides
/// who may act on one. A user may act on a task assigned to their id, or to a role they hold. Ids
/// and role names match exactly, including letter case.
/// </summary>
public static class TaskQueries
{
    private const string Columns =
        """
        id, process_instance_id, process_id, release_id, application_id, step, subject_entity_id, subject_id,
        assignee_kind, assignee, form_id, due_at, state, outcome, completed_by, completed_at, created_at
        """;

    private const string MayActWhere =
        $"""
        application_id = @application AND state = '{ProcessTasks.Open}'
          AND ((assignee_kind = '{ProcessTasks.UserAssignee}' AND assignee = @user)
            OR (assignee_kind = '{ProcessTasks.RoleAssignee}' AND assignee = ANY(@roles)))
        """;

    /// <summary>
    /// Reads page <paramref name="page"/> of <paramref name="pageSize"/> open tasks of the
    /// application that <paramref name="userId"/> may act on, whatever release their instance is
    /// pinned to, and counts them. Tasks are ordered by due date with no due date last, then by
    /// creation time, then by id. A page past the last one has no items.
    /// </summary>
    public static async Task<TaskPage> ListOpenAsync(
        NpgsqlConnection connection,
        Guid applicationId,
        string userId,
        IReadOnlyCollection<string> roles,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        long totalCount;
        await using (var count = new NpgsqlCommand($"SELECT count(*) FROM axis.process_tasks WHERE {MayActWhere}", connection))
        {
            AddMayActParameters(count, applicationId, userId, roles);
            totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

        await using var command = new NpgsqlCommand(
            $"""
            SELECT {Columns} FROM axis.process_tasks WHERE {MayActWhere}
            ORDER BY due_at ASC NULLS LAST, created_at, id
            LIMIT @limit OFFSET @offset
            """,
            connection);
        AddMayActParameters(command, applicationId, userId, roles);
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

    /// <summary>Reads the task with <paramref name="id"/> in any state, or <see langword="null"/> when there is none.</summary>
    public static async Task<ProcessTaskRow?> FindAsync(NpgsqlConnection connection, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand($"SELECT {Columns} FROM axis.process_tasks WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTask(reader) : null;
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

    private static void AddMayActParameters(NpgsqlCommand command, Guid applicationId, string userId, IReadOnlyCollection<string> roles)
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
        };
}

/// <summary>One page of the tasks a user may act on, and the number of them on every page.</summary>
public sealed record TaskPage(IReadOnlyList<ProcessTaskRow> Items, long TotalCount);
