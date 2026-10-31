using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Records;
using Axis.Processes.Storage;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>How a task reassignment ended.</summary>
public enum TaskReassignOutcome
{
    /// <summary>The task is handed over, its claim is cleared and the audit record is appended. The caller commits.</summary>
    Done,

    /// <summary>The task does not exist, or belongs to another application.</summary>
    NotFound,

    /// <summary>The task is not open.</summary>
    NotOpen,
}

/// <summary>
/// Hands a locked task to a role or a user in the caller's transaction. It checks that the task exists
/// in the application (not found) and that it is open (not open), then sets the assignee, clears the
/// claim and appends the audit record <c>task.reassigned</c>. Its details hold the task id, the old
/// and new assignee, each as <c>{ "role": name }</c> or <c>{ "user": id }</c>, and the reason. The caller
/// has already checked that the actor holds <c>reassign</c> on the task's process and that the target
/// is a declared role or a user with a profile.
/// The command never commits or rolls back. A result other than <see cref="TaskReassignOutcome.Done"/>
/// writes nothing.
/// </summary>
public static class TaskReassignCommand
{
    /// <param name="transaction">The transaction that holds the task's lock.</param>
    /// <param name="applicationId">The application the request names.</param>
    /// <param name="task">The task as <see cref="TaskQueries.LockAsync"/> returned it.</param>
    /// <param name="assigneeKind"><see cref="ProcessTasks.RoleAssignee"/> or <see cref="ProcessTasks.UserAssignee"/>.</param>
    /// <param name="assignee">The role name or the user id.</param>
    /// <param name="reason">Why the task is handed over.</param>
    /// <param name="actor">The user who reassigns the task.</param>
    public static async Task<TaskReassignOutcome> ReassignLockedAsync(
        NpgsqlTransaction transaction,
        Guid applicationId,
        ProcessTaskRow? task,
        string assigneeKind,
        string assignee,
        string reason,
        RecordActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(assigneeKind);
        ArgumentNullException.ThrowIfNull(assignee);
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(actor);

        if (task is null || task.ApplicationId != applicationId)
        {
            return TaskReassignOutcome.NotFound;
        }

        if (task.State != ProcessTasks.Open
            || !await ProcessTasks.TryReassignAsync(transaction, task.Id, assigneeKind, assignee, cancellationToken))
        {
            return TaskReassignOutcome.NotOpen;
        }

        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                actor.Id,
                AuditActions.TaskReassigned,
                task.ApplicationId,
                task.SubjectEntityId,
                task.SubjectId,
                task.ProcessInstanceId,
                new JsonObject
                {
                    ["taskId"] = task.Id,
                    ["from"] = new JsonObject { [task.AssigneeKind] = task.Assignee },
                    ["to"] = new JsonObject { [assigneeKind] = assignee },
                    ["reason"] = reason,
                }),
            cancellationToken);
        return TaskReassignOutcome.Done;
    }
}
