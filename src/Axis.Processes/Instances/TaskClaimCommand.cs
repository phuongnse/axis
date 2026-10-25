using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Records;
using Axis.Processes.Storage;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>How a task claim or release ended.</summary>
public enum TaskClaimOutcome
{
    /// <summary>The claim is set or cleared and the audit record is appended. The caller commits.</summary>
    Done,

    /// <summary>The task does not exist, or belongs to another application.</summary>
    NotFound,

    /// <summary>The actor may not act on the task, or may not release a claim another holder made.</summary>
    Denied,

    /// <summary>The task is not open.</summary>
    NotOpen,

    /// <summary>The task is assigned to a user, so it cannot be claimed.</summary>
    NotRoleTask,

    /// <summary>The task is already claimed, by the actor or by another holder.</summary>
    AlreadyClaimed,

    /// <summary>The task is not claimed, so there is nothing to release.</summary>
    NotClaimed,
}

/// <summary>
/// Claims or releases a locked role task in the caller's transaction, as the task API does. A claim
/// checks, in order, that the task exists in the application (not found), that the actor may act on
/// it (denied), that it is open (not open), that it is assigned to a role (not a role task) and that
/// it is not claimed (already claimed). A release checks that the task exists, that the actor may
/// act on it, that it is open, that it is claimed (not claimed) and that the actor is the claimer
/// (denied). Each then writes the claim and appends the audit record <c>task.claimed</c> or
/// <c>task.released</c>. A role assignee counts only when <see cref="RecordActor.Roles"/>, the
/// roles the actor holds in the active release, include it.
/// The command never commits or rolls back. A result other than <see cref="TaskClaimOutcome.Done"/>
/// writes nothing.
/// </summary>
public static class TaskClaimCommand
{
    /// <param name="transaction">The transaction that holds the task's lock.</param>
    /// <param name="applicationId">The application the request names.</param>
    /// <param name="task">The task as <see cref="TaskQueries.LockAsync"/> returned it.</param>
    /// <param name="actor">The user claiming the task. <see cref="RecordActor.Roles"/> are the roles the user holds in the active release.</param>
    public static async Task<TaskClaimOutcome> ClaimLockedAsync(
        NpgsqlTransaction transaction,
        Guid applicationId,
        ProcessTaskRow? task,
        RecordActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(actor);

        if (task is null || task.ApplicationId != applicationId)
        {
            return TaskClaimOutcome.NotFound;
        }

        if (!TaskQueries.MayAct(task, actor.Id, actor.Roles))
        {
            return TaskClaimOutcome.Denied;
        }

        if (task.State != ProcessTasks.Open)
        {
            return TaskClaimOutcome.NotOpen;
        }

        if (task.AssigneeKind != ProcessTasks.RoleAssignee)
        {
            return TaskClaimOutcome.NotRoleTask;
        }

        if (task.ClaimedBy is not null
            || !await ProcessTasks.TryClaimAsync(transaction, task.Id, actor.Id, cancellationToken))
        {
            return TaskClaimOutcome.AlreadyClaimed;
        }

        await AppendAsync(transaction, AuditActions.TaskClaimed, task, actor, cancellationToken);
        return TaskClaimOutcome.Done;
    }

    /// <inheritdoc cref="ClaimLockedAsync"/>
    public static async Task<TaskClaimOutcome> ReleaseLockedAsync(
        NpgsqlTransaction transaction,
        Guid applicationId,
        ProcessTaskRow? task,
        RecordActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(actor);

        if (task is null || task.ApplicationId != applicationId)
        {
            return TaskClaimOutcome.NotFound;
        }

        if (!TaskQueries.MayAct(task, actor.Id, actor.Roles))
        {
            return TaskClaimOutcome.Denied;
        }

        if (task.State != ProcessTasks.Open)
        {
            return TaskClaimOutcome.NotOpen;
        }

        if (task.ClaimedBy is null)
        {
            return TaskClaimOutcome.NotClaimed;
        }

        if (!string.Equals(task.ClaimedBy, actor.Id, StringComparison.Ordinal))
        {
            return TaskClaimOutcome.Denied;
        }

        if (!await ProcessTasks.TryReleaseAsync(transaction, task.Id, cancellationToken))
        {
            return TaskClaimOutcome.NotClaimed;
        }

        await AppendAsync(transaction, AuditActions.TaskReleased, task, actor, cancellationToken);
        return TaskClaimOutcome.Done;
    }

    private static Task AppendAsync(
        NpgsqlTransaction transaction, string action, ProcessTaskRow task, RecordActor actor, CancellationToken cancellationToken) =>
        AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                actor.Id,
                action,
                task.ApplicationId,
                task.SubjectEntityId,
                task.SubjectId,
                task.ProcessInstanceId,
                new JsonObject { ["taskId"] = task.Id },
                actor.DisplayName),
            cancellationToken);
}
