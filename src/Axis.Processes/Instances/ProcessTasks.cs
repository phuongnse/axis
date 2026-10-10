using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Writes the human tasks of process instances over a connection to a tenant database, and names
/// their states and assignee kinds.
/// </summary>
public static class ProcessTasks
{
    /// <summary>The state of a task that waits for a decision.</summary>
    public const string Open = "open";

    /// <summary>The state of a task whose decision is recorded.</summary>
    public const string Completed = "completed";

    /// <summary>The assignee kind of a task assigned to one user id.</summary>
    public const string UserAssignee = "user";

    /// <summary>The assignee kind of a task assigned to a role name.</summary>
    public const string RoleAssignee = "role";

    /// <summary>Inserts <paramref name="task"/> as <c>open</c>. It is created at the transaction's time.</summary>
    internal static async Task InsertAsync(NpgsqlTransaction transaction, NewTask task, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.process_tasks
                (id, process_instance_id, process_id, release_id, application_id, step, subject_entity_id, subject_id,
                 assignee_kind, assignee, form_id, due_at, state, created_at)
            VALUES (@id, @instance, @process, @release, @application, @step, @entity, @subject,
                    @assigneeKind, @assignee, @form, @due, @state, now())
            """,
            transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction)),
            transaction);
        command.Parameters.AddWithValue("id", task.Id);
        command.Parameters.AddWithValue("instance", task.ProcessInstanceId);
        command.Parameters.AddWithValue("process", task.ProcessId);
        command.Parameters.AddWithValue("release", task.ReleaseId);
        command.Parameters.AddWithValue("application", task.ApplicationId);
        command.Parameters.AddWithValue("step", task.Step);
        command.Parameters.AddWithValue("entity", task.SubjectEntityId);
        command.Parameters.AddWithValue("subject", task.SubjectId);
        command.Parameters.AddWithValue("assigneeKind", task.AssigneeKind);
        command.Parameters.AddWithValue("assignee", task.Assignee);
        command.Parameters.AddWithValue("form", task.FormId);
        command.Parameters.Add(new NpgsqlParameter("due", NpgsqlDbType.TimestampTz) { Value = (object?)task.DueAt?.ToUniversalTime() ?? DBNull.Value });
        command.Parameters.AddWithValue("state", Open);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>A task that a <c>task</c> step creates for its instance.</summary>
/// <param name="AssigneeKind"><c>user</c> or <c>role</c>.</param>
/// <param name="Assignee">The user id or the role name.</param>
/// <param name="DueAt">The task's due date, or <see langword="null"/> when the step has no <c>dueIn</c>.</param>
internal sealed record NewTask(
    Guid Id,
    Guid ProcessInstanceId,
    Guid ProcessId,
    Guid ReleaseId,
    Guid ApplicationId,
    string Step,
    Guid SubjectEntityId,
    Guid SubjectId,
    string AssigneeKind,
    string Assignee,
    Guid FormId,
    DateTimeOffset? DueAt);
