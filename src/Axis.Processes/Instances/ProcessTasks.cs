using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Writes the human tasks of process instances over a connection to a tenant database, and names
/// their states and assignee kinds. A completion closes the task, deletes its escalation and deadline items and
/// resumes its instance in the caller's transaction.
/// </summary>
public static class ProcessTasks
{
    /// <summary>The state of a task that waits for a decision.</summary>
    public const string Open = "open";

    /// <summary>The state of a task whose decision is recorded.</summary>
    public const string Completed = "completed";

    /// <summary>The state of a task that an operator's cancel of its instance closed. It has no outcome.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The state of a task whose deadline passed while it was open. It has no outcome.</summary>
    public const string Expired = "expired";

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

    /// <summary>
    /// Sets <paramref name="userId"/> as the claimer of the <c>open</c> task <paramref name="id"/>
    /// at the transaction's time. Returns <see langword="false"/> when the task is missing, no
    /// longer open or already claimed. Lock the task first with <see cref="TaskQueries.LockAsync"/>.
    /// </summary>
    public static async Task<bool> TryClaimAsync(
        NpgsqlTransaction transaction,
        Guid id,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(userId);

        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.process_tasks
            SET claimed_by = @user, claimed_at = now()
            WHERE id = @id AND state = @open AND claimed_by IS NULL
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("open", Open);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>
    /// Clears the claim of the <c>open</c> task <paramref name="id"/>. Returns <see langword="false"/>
    /// when the task is missing, no longer open or not claimed. Lock the task first with
    /// <see cref="TaskQueries.LockAsync"/>.
    /// </summary>
    public static async Task<bool> TryReleaseAsync(
        NpgsqlTransaction transaction,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.process_tasks
            SET claimed_by = NULL, claimed_at = NULL
            WHERE id = @id AND state = @open AND claimed_by IS NOT NULL
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("open", Open);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>
    /// Hands the <c>open</c> task <paramref name="id"/> to <paramref name="assignee"/>, a user id or a role name
    /// as <paramref name="assigneeKind"/> says, and clears its claim. Returns <see langword="false"/> when the task is
    /// missing or no longer open. Lock the task first with <see cref="TaskQueries.LockAsync"/>.
    /// </summary>
    public static async Task<bool> TryReassignAsync(
        NpgsqlTransaction transaction,
        Guid id,
        string assigneeKind,
        string assignee,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(assigneeKind);
        ArgumentNullException.ThrowIfNull(assignee);

        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.process_tasks
            SET assignee_kind = @kind, assignee = @assignee, claimed_by = NULL, claimed_at = NULL
            WHERE id = @id AND state = @open
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("kind", assigneeKind);
        command.Parameters.AddWithValue("assignee", assignee);
        command.Parameters.AddWithValue("open", Open);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>
    /// Sets the <c>open</c> task <paramref name="id"/> <c>completed</c> with <paramref name="outcome"/>,
    /// by <paramref name="userId"/>, at the transaction's time. Returns <see langword="false"/> when
    /// the task is missing or no longer open. Lock the task first with <see cref="TaskQueries.LockAsync"/>,
    /// so this only backs that check up.
    /// </summary>
    public static async Task<bool> TryCompleteAsync(
        NpgsqlTransaction transaction,
        Guid id,
        string outcome,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(userId);

        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.process_tasks
            SET state = @completed, outcome = @outcome, completed_by = @user, completed_at = now()
            WHERE id = @id AND state = @open
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("outcome", outcome);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("completed", Completed);
        command.Parameters.AddWithValue("open", Open);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>
    /// Sets the <c>open</c> task <paramref name="id"/> <c>expired</c> at the transaction's time.
    /// Returns <see langword="false"/> when the task is missing or no longer open. Lock the task
    /// first with <see cref="TaskQueries.LockAsync"/>, so this only backs that check up.
    /// </summary>
    internal static async Task<bool> TryExpireAsync(NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = new NpgsqlCommand(
            "UPDATE axis.process_tasks SET state = @expired, expired_at = now() WHERE id = @id AND state = @open",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("expired", Expired);
        command.Parameters.AddWithValue("open", Open);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Deletes the work items that wait on the task <paramref name="taskId"/>, which are its pending <c>task.escalate</c> and <c>task.deadline</c> items.</summary>
    internal static async Task DeletePendingItemsAsync(NpgsqlTransaction transaction, Guid taskId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = new NpgsqlCommand(
            "DELETE FROM axis.process_work_items WHERE task_id = @task",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("task", taskId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes the pending <c>task.escalate</c> items of the task <paramref name="taskId"/>. An expiry
    /// calls it from the deadline's own item, which it keeps, since the worker deletes that item last.
    /// </summary>
    internal static async Task DeletePendingEscalationsAsync(NpgsqlTransaction transaction, Guid taskId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = new NpgsqlCommand(
            "DELETE FROM axis.process_work_items WHERE task_id = @task AND kind = @kind",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("task", taskId);
        command.Parameters.AddWithValue("kind", ProcessStarts.TaskEscalateWorkItemKind);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Resumes the instance of the completed <paramref name="task"/> on the outcome's next step: the
    /// instance becomes <c>running</c> there with the next revision, the task step gets its history
    /// row, the audit record <c>task.completed</c> is appended with the user and their display name
    /// as its actor, and the work item of the next step is queued for <paramref name="tenantId"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The instance is not waiting on the task's step.</exception>
    public static async Task ResumeAsync(
        NpgsqlTransaction transaction,
        ProcessTaskRow task,
        TaskCompletion completion,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(completion);
        ArgumentNullException.ThrowIfNull(tenantId);

        var revision = await ProcessSteps.TryResumeAsync(transaction, task.ProcessInstanceId, task.Step, completion.Next, cancellationToken)
            ?? throw new InvalidOperationException($"The instance of task '{task.Id}' is not waiting on the task's step.");

        var connection = Connection(transaction);
        var output = new JsonObject { ["taskId"] = task.Id, ["outcome"] = completion.Outcome, ["next"] = completion.Next };
        if (completion.WrittenVersion is { } written)
        {
            output["subjectVersion"] = written;
        }

        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(
                task.ProcessInstanceId,
                task.Step,
                revision,
                new JsonObject { ["subjectId"] = task.SubjectId, ["subjectVersion"] = completion.SubjectVersion },
                output,
                completion.Outcome,
                null,
                await RecordQueries.TransactionTimeAsync(connection, cancellationToken)),
            cancellationToken);
        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                completion.UserId,
                AuditActions.TaskCompleted,
                task.ApplicationId,
                task.SubjectEntityId,
                task.SubjectId,
                task.ProcessInstanceId,
                new JsonObject
                {
                    ["outcome"] = completion.Outcome,
                    ["fields"] = new JsonArray([.. completion.Fields.Select(field => JsonValue.Create(field))]),
                }),
            cancellationToken);
        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            tenantId,
            ProcessStarts.StepWorkItemKind,
            DateTimeOffset.UtcNow,
            cancellationToken,
            processInstanceId: task.ProcessInstanceId);
    }

    private static NpgsqlConnection Connection(NpgsqlTransaction transaction) =>
        transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
}

/// <summary>The decision a task was completed with, as its instance resumes on it.</summary>
/// <param name="Outcome">The declared name of the outcome.</param>
/// <param name="Next">The declared name of the step the outcome leads to.</param>
/// <param name="UserId">The user who completed the task.</param>
/// <param name="UserName">The display name of the user who completed the task, stored in its audit record.</param>
/// <param name="SubjectVersion">The subject record version the values were written over, or <see langword="null"/> when no values were written.</param>
/// <param name="WrittenVersion">The subject record version the values wrote, or <see langword="null"/> when no values were written.</param>
/// <param name="Fields">The declared names of the fields the completion set, in declaration order.</param>
public sealed record TaskCompletion(
    string Outcome,
    string Next,
    string UserId,
    string UserName,
    long? SubjectVersion,
    long? WrittenVersion,
    IReadOnlyList<string> Fields);

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
