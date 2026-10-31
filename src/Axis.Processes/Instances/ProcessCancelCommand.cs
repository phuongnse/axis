using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Records;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>How a cancel ended.</summary>
public enum ProcessCancelOutcome
{
    /// <summary>The instance, its open tasks and its pending calls are cancelled. The caller commits.</summary>
    Cancelled,

    /// <summary>
    /// The instance does not exist for the process, or its subject record is not readable by the user. An
    /// instance without a subject has no record to read.
    /// </summary>
    NotFound,

    /// <summary>The instance is not <c>running</c> or <c>waiting</c>.</summary>
    NotActive,

    /// <summary>A call of the instance is <c>sending</c> or <c>unknown</c>, see <see cref="ProcessCancelResult.CallId"/>.</summary>
    CallInFlight,

    /// <summary>
    /// A <c>read</c> filter of the user reads a computed field that is being recomputed, so no instance can be
    /// checked against it. Nothing was written.
    /// </summary>
    PendingFilter,
}

/// <summary>
/// The result of a cancel. <see cref="CallId"/> is set for <see cref="ProcessCancelOutcome.CallInFlight"/>.
/// <see cref="ReleaseId"/> and <see cref="SubjectId"/> are set for <see cref="ProcessCancelOutcome.Cancelled"/>.
/// </summary>
public sealed record ProcessCancelResult(
    ProcessCancelOutcome Outcome,
    Guid? CallId = null,
    Guid? ReleaseId = null,
    Guid? SubjectId = null);

/// <summary>
/// Cancels a <c>running</c> or <c>waiting</c> instance, and its running or waiting sub-processes, in the
/// caller's transaction. The user's <c>cancel</c> grant is the caller's check. The instance's subject
/// record must be readable by the user, or the instance does not exist for them. An instance without a
/// subject has no record, so the grant is the only check. When a <c>read</c>
/// filter of the user reads a computed field that is being recomputed, the cancel is refused first,
/// before the instance is found.
/// The statements run in one order. An instance that has a parent locks the parent row first, so a
/// cancel of a child and a cancel of its parent meet on the parent. Then come the instance's open
/// tasks, the instance, and its connector and package calls. A task completion locks its task before
/// the instance, so locking the instance first would deadlock with it. The cancel repeats tasks, instance
/// and calls for each running or waiting child, depth first, before it writes anything. A call that is
/// <c>sending</c> or <c>unknown</c> in the instance or in any child may have reached the other system, and
/// stops the cancel. Otherwise each instance becomes <c>cancelled</c> at the next revision, which
/// fences a step a worker already loaded. Its open tasks become <c>cancelled</c> with no outcome.
/// Its <c>pending</c> calls become <c>cancelled</c> and their stored input is cleared. Its work
/// items are deleted. A history row and the audit record <c>process.cancelled</c> are written, with
/// the same user and reason for every instance of the tree. When the cancelled instance is a child and
/// its parent is <c>waiting</c> on the sub-process step for this child, the parent fails with a fixed error,
/// a history row and the audit record <c>process.failed</c> as <c>system</c>. A parent that is being
/// cancelled is not <c>waiting</c>, so it ends as <c>cancelled</c>.
/// The command never commits or rolls back. A result other than <see cref="ProcessCancelOutcome.Cancelled"/>
/// may have locked rows, so the caller rolls back.
/// </summary>
public static class ProcessCancelCommand
{
    /// <param name="active">The active release's model, which the user's read filters are evaluated in.</param>
    /// <param name="process">The process of the route. The instance must belong to it.</param>
    /// <param name="entity">The subject entity of <paramref name="process"/> in <paramref name="active"/>, or null for a process without a subject.</param>
    /// <param name="read">The user's <c>read</c> access to <paramref name="entity"/>, or null for a process without a subject.</param>
    public static async Task<ProcessCancelResult> CancelAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApplicationModel active,
        ProcessModel process,
        EntityModel? entity,
        Guid instanceId,
        RecordAccess? read,
        string userId,
        string reason,
        RecordCipher? cipher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // A read filter on a field that is being recomputed decides nothing. It is checked before any row is
        // locked, and before the instance is looked up, so it tells nothing about the instance.
        if (entity is not null
            && read?.Filters is { } pendingFilters
            && await RecordCommand.ReadsPendingAsync(connection, transaction, active, entity, pendingFilters, loggerFactory, cancellationToken))
        {
            return new ProcessCancelResult(ProcessCancelOutcome.PendingFilter);
        }

        // The parent comes first, because a cancel of the parent locks the parent and then the child.
        var parentId = await ReadParentAsync(transaction, instanceId, cancellationToken);
        var parent = parentId is { } id ? await LockParentAsync(transaction, id, cancellationToken) : null;

        // The open tasks come next, because a task completion locks its task and then the instance.
        await LockOpenTasksAsync(transaction, instanceId, cancellationToken);

        var instance = await LockInstanceAsync(transaction, instanceId, cancellationToken);

        if (instance is null || instance.ApplicationId != active.Manifest.Id || instance.ProcessId != process.Id)
        {
            return new ProcessCancelResult(ProcessCancelOutcome.NotFound);
        }

        // An instance without a subject has no record to read, so only the grant applies. An instance with a
        // subject is never exempt from the read check, whatever the process's subject is now.
        if (instance.SubjectId is { } subjectId
            && (entity is null
                || read is not { Granted: true }
                || (read.Filters is { } filters
                    && await RecordCommand.MatchAsync(connection, transaction, active, entity, subjectId, filters, userId, cipher, loggerFactory, cancellationToken)
                        is not { Found: true, Matched: true })))
        {
            return new ProcessCancelResult(ProcessCancelOutcome.NotFound);
        }

        if (instance.State is not (ProcessStarts.Running or ProcessStarts.Waiting))
        {
            return new ProcessCancelResult(ProcessCancelOutcome.NotActive);
        }

        var result = await CancelTreeAsync(connection, transaction, instanceId, userId, reason, cancellationToken);
        if (result.Outcome == ProcessCancelOutcome.Cancelled && parent is not null)
        {
            await FailParentAsync(connection, transaction, parentId!.Value, parent, instance.ParentStep, instanceId, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Cancels the running or waiting instance <paramref name="instanceId"/> and its running or waiting
    /// children, in two phases. The first locks every instance of the tree with its tasks and calls and
    /// returns <see cref="ProcessCancelOutcome.CallInFlight"/> before any write. The second writes in lock
    /// order. The caller has locked the instance, found it active and checked the user's access.
    /// </summary>
    internal static async Task<ProcessCancelResult> CancelTreeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid instanceId,
        string userId,
        string reason,
        CancellationToken cancellationToken)
    {
        var tree = new LockedTree();
        await LockTreeAsync(transaction, instanceId, tree, cancellationToken);
        if (tree.InFlight is { } inFlight)
        {
            return new ProcessCancelResult(ProcessCancelOutcome.CallInFlight, inFlight);
        }

        var root = tree.Instances.FirstOrDefault(locked => locked.Id == instanceId)
            ?? throw new InvalidOperationException($"The locked instance '{instanceId}' is not running or waiting.");
        var startedAt = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        foreach (var instance in tree.Instances)
        {
            await CancelOneAsync(transaction, instance, userId, reason, startedAt, cancellationToken);
        }

        return new ProcessCancelResult(ProcessCancelOutcome.Cancelled, ReleaseId: root.ReleaseId, SubjectId: root.SubjectId);
    }

    // Locks the open tasks, the instance and its calls, then goes on with its running or waiting children.
    private static async Task LockTreeAsync(NpgsqlTransaction transaction, Guid instanceId, LockedTree tree, CancellationToken cancellationToken)
    {
        await LockOpenTasksAsync(transaction, instanceId, cancellationToken);
        var instance = await LockInstanceAsync(transaction, instanceId, cancellationToken);
        if (instance is not { State: ProcessStarts.Running or ProcessStarts.Waiting })
        {
            return;
        }

        tree.Instances.Add(instance);
        if (await FindCallInFlightAsync(transaction, instanceId, cancellationToken) is { } inFlight)
        {
            tree.InFlight ??= inFlight;
        }

        foreach (var childId in await FindChildrenAsync(transaction, instanceId, cancellationToken))
        {
            await LockTreeAsync(transaction, childId, tree, cancellationToken);
        }
    }

    private static async Task CancelOneAsync(
        NpgsqlTransaction transaction,
        LockedInstance instance,
        string userId,
        string reason,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var revision = await CancelInstanceAsync(transaction, instance.Id, cancellationToken)
            ?? throw new InvalidOperationException($"The locked instance '{instance.Id}' could not be cancelled.");
        var taskIds = await CancelTasksAsync(transaction, instance.Id, cancellationToken);
        var operationIds = await CancelCallsAsync(transaction, "axis.connector_calls", instance.Id, cancellationToken);
        operationIds.AddRange(await CancelCallsAsync(transaction, "axis.package_calls", instance.Id, cancellationToken));
        await DeleteWorkItemsAsync(transaction, instance.Id, cancellationToken);

        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(
                instance.Id,
                instance.Step,
                revision,
                new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = null },
                new JsonObject { ["command"] = "cancel" },
                Decision: null,
                Error: null,
                startedAt),
            cancellationToken);

        var details = new JsonObject
        {
            ["step"] = instance.Step,
            ["reason"] = reason,
            ["taskIds"] = new JsonArray([.. taskIds.Select(id => (JsonNode?)JsonValue.Create(id))]),
            ["operationIds"] = new JsonArray([.. operationIds.Select(id => (JsonNode?)JsonValue.Create(id))]),
        };
        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                userId,
                AuditActions.ProcessCancelled,
                instance.ApplicationId,
                instance.SubjectEntityId,
                instance.SubjectId,
                instance.Id,
                details),
            cancellationToken);
    }

    // Fails the parent when it waits on its sub-process step for this child. The latest history row of the step
    // names the child it waits for, so an old child of a looped step does not fail a newer wait.
    private static async Task FailParentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parentId,
        LockedParent parent,
        string? parentStep,
        Guid childId,
        CancellationToken cancellationToken)
    {
        if (parent.State != ProcessStarts.Waiting || parentStep is null || parent.Step != parentStep)
        {
            return;
        }

        var waiting = await ProcessSteps.FindLatestOutputAsync(transaction, parentId, parentStep, cancellationToken);
        if (!Guid.TryParse(waiting?["childInstanceId"]?.GetValue<string>(), out var waitedFor) || waitedFor != childId)
        {
            return;
        }

        if (!await ProcessSteps.TryFailWaitingAsync(transaction, parentId, parentStep, parent.Revision, cancellationToken))
        {
            throw new InvalidOperationException($"The locked parent '{parentId}' could not be failed.");
        }

        var startedAt = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(
                parentId,
                parentStep,
                parent.Revision,
                new JsonObject { ["subjectId"] = parent.SubjectId, ["subjectVersion"] = null },
                new JsonObject { ["childInstanceId"] = childId },
                Decision: null,
                Error: $"The sub-process of the step '{parentStep}' was cancelled.",
                startedAt),
            cancellationToken);
        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                AuditActors.System,
                AuditActions.ProcessFailed,
                parent.ApplicationId,
                parent.SubjectEntityId,
                parent.SubjectId,
                parentId,
                new JsonObject { ["step"] = parentStep }),
            cancellationToken);
    }

    private static async Task<Guid?> ReadParentAsync(NpgsqlTransaction transaction, Guid instanceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT parent_instance_id FROM axis.process_instances WHERE id = @id",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", instanceId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid parentId ? parentId : null;
    }

    private static async Task<LockedParent?> LockParentAsync(NpgsqlTransaction transaction, Guid parentId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT state, step, revision, application_id, subject_entity_id, subject_id
            FROM axis.process_instances WHERE id = @id FOR UPDATE
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", parentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LockedParent(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5));
    }

    private static async Task<List<Guid>> FindChildrenAsync(NpgsqlTransaction transaction, Guid instanceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT id FROM axis.process_instances WHERE parent_instance_id = @id AND state IN (@running, @waiting) ORDER BY id",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", instanceId);
        command.Parameters.AddWithValue("running", ProcessStarts.Running);
        command.Parameters.AddWithValue("waiting", ProcessStarts.Waiting);
        return await ReadIdsAsync(command, cancellationToken);
    }

    private static async Task LockOpenTasksAsync(NpgsqlTransaction transaction, Guid instanceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT id FROM axis.process_tasks WHERE process_instance_id = @instance AND state = @open ORDER BY id FOR UPDATE",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("instance", instanceId);
        command.Parameters.AddWithValue("open", ProcessTasks.Open);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
        }
    }

    private static async Task<LockedInstance?> LockInstanceAsync(NpgsqlTransaction transaction, Guid instanceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT application_id, process_id, subject_entity_id, subject_id, release_id, state, step, parent_step
            FROM axis.process_instances WHERE id = @id FOR UPDATE
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LockedInstance(
            instanceId,
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    // Locks every call of the instance, connector calls first, and returns the first one that may have reached the other system.
    private static async Task<Guid?> FindCallInFlightAsync(NpgsqlTransaction transaction, Guid instanceId, CancellationToken cancellationToken)
    {
        Guid? inFlight = null;
        foreach (var table in new[] { "axis.connector_calls", "axis.package_calls" })
        {
            await using var command = new NpgsqlCommand(
                $"SELECT id, state FROM {table} WHERE process_instance_id = @instance ORDER BY id FOR UPDATE",
                Connection(transaction),
                transaction);
            command.Parameters.AddWithValue("instance", instanceId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (inFlight is null && reader.GetString(1) is ConnectorCalls.Sending or ConnectorCalls.Unknown)
                {
                    inFlight = reader.GetGuid(0);
                }
            }
        }

        return inFlight;
    }

    // Returns the revision the instance had before the cancel.
    private static async Task<long?> CancelInstanceAsync(NpgsqlTransaction transaction, Guid instanceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.process_instances
            SET state = @cancelled, revision = revision + 1, ended_at = now()
            WHERE id = @id AND state IN (@running, @waiting)
            RETURNING revision - 1
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", instanceId);
        command.Parameters.AddWithValue("cancelled", ProcessStarts.Cancelled);
        command.Parameters.AddWithValue("running", ProcessStarts.Running);
        command.Parameters.AddWithValue("waiting", ProcessStarts.Waiting);
        return await command.ExecuteScalarAsync(cancellationToken) is long revision ? revision : null;
    }

    // A fresh statement, so a task that committed while the instance lock was awaited is closed too.
    private static async Task<List<Guid>> CancelTasksAsync(NpgsqlTransaction transaction, Guid instanceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE axis.process_tasks SET state = @cancelled WHERE process_instance_id = @instance AND state = @open RETURNING id",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("instance", instanceId);
        command.Parameters.AddWithValue("cancelled", ProcessTasks.Cancelled);
        command.Parameters.AddWithValue("open", ProcessTasks.Open);
        return await ReadIdsAsync(command, cancellationToken);
    }

    private static async Task<List<Guid>> CancelCallsAsync(NpgsqlTransaction transaction, string table, Guid instanceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"UPDATE {table} SET state = @cancelled, input = NULL WHERE process_instance_id = @instance AND state = @pending RETURNING id",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("instance", instanceId);
        command.Parameters.AddWithValue("cancelled", ConnectorCalls.Cancelled);
        command.Parameters.AddWithValue("pending", ConnectorCalls.Pending);
        return await ReadIdsAsync(command, cancellationToken);
    }

    private static async Task DeleteWorkItemsAsync(NpgsqlTransaction transaction, Guid instanceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "DELETE FROM axis.process_work_items WHERE process_instance_id = @instance",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("instance", instanceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<Guid>> ReadIdsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private static NpgsqlConnection Connection(NpgsqlTransaction transaction) =>
        transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));

    // The subject ids are null for an instance of a process without a subject.
    private sealed record LockedInstance(
        Guid Id,
        Guid ApplicationId,
        Guid ProcessId,
        Guid? SubjectEntityId,
        Guid? SubjectId,
        Guid ReleaseId,
        string State,
        string Step,
        string? ParentStep);

    private sealed record LockedParent(
        string State,
        string Step,
        long Revision,
        Guid ApplicationId,
        Guid? SubjectEntityId,
        Guid? SubjectId);

    // The instances to cancel in lock order, and the first call of the tree that is sending or unknown.
    private sealed class LockedTree
    {
        public List<LockedInstance> Instances { get; } = [];

        public Guid? InFlight { get; set; }
    }
}
