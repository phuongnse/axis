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

    /// <summary>The instance does not exist for the process, or its subject record is not readable by the user.</summary>
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
/// Cancels a <c>running</c> or <c>waiting</c> instance in the caller's transaction. The user's
/// <c>cancel</c> grant is the caller's check. The instance's subject record must be readable by
/// the user, or the instance does not exist for them. When a <c>read</c> filter of the user reads a
/// computed field that is being recomputed, the cancel is refused first, before the instance is found.
/// The statements run in one order: the
/// instance's open tasks, then the instance, then its connector and package calls. A task
/// completion locks its task before the instance, so locking the instance first would deadlock
/// with it. A call that is <c>sending</c> or <c>unknown</c> may have reached the other system, and
/// stops the cancel. Otherwise the instance becomes <c>cancelled</c> at the next revision, which
/// fences a step a worker already loaded. Its open tasks become <c>cancelled</c> with no outcome.
/// Its <c>pending</c> calls become <c>cancelled</c> and their stored input is cleared. Its work
/// items are deleted. A history row and the audit record <c>process.cancelled</c> are written.
/// The command never commits or rolls back. A result other than <see cref="ProcessCancelOutcome.Cancelled"/>
/// may have locked rows, so the caller rolls back.
/// </summary>
public static class ProcessCancelCommand
{
    /// <param name="active">The active release's model, which the user's read filters are evaluated in.</param>
    /// <param name="process">The process of the route. The instance must belong to it.</param>
    /// <param name="entity">The subject entity of <paramref name="process"/> in <paramref name="active"/>.</param>
    /// <param name="read">The user's <c>read</c> access to <paramref name="entity"/>.</param>
    public static async Task<ProcessCancelResult> CancelAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApplicationModel active,
        ProcessModel process,
        EntityModel entity,
        Guid instanceId,
        RecordAccess read,
        string userId,
        string userName,
        string reason,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // A read filter on a field that is being recomputed decides nothing. It is checked before any row is
        // locked, and before the instance is looked up, so it tells nothing about the instance.
        if (read.Filters is { } pendingFilters
            && await RecordCommand.ReadsPendingAsync(connection, transaction, active, entity, pendingFilters, loggerFactory, cancellationToken))
        {
            return new ProcessCancelResult(ProcessCancelOutcome.PendingFilter);
        }

        // The open tasks come first, because a task completion locks its task and then the instance.
        await LockOpenTasksAsync(transaction, instanceId, cancellationToken);

        var instance = await LockInstanceAsync(transaction, instanceId, cancellationToken);
        if (instance is null || instance.ApplicationId != active.Manifest.Id || instance.ProcessId != process.Id)
        {
            return new ProcessCancelResult(ProcessCancelOutcome.NotFound);
        }

        if (!read.Granted
            || (read.Filters is { } filters
                && await RecordCommand.MatchAsync(connection, transaction, active, entity, instance.SubjectId, filters, userId, loggerFactory, cancellationToken)
                    is not { Found: true, Matched: true }))
        {
            return new ProcessCancelResult(ProcessCancelOutcome.NotFound);
        }

        if (instance.State is not (ProcessStarts.Running or ProcessStarts.Waiting))
        {
            return new ProcessCancelResult(ProcessCancelOutcome.NotActive);
        }

        if (await FindCallInFlightAsync(transaction, instanceId, cancellationToken) is { } inFlight)
        {
            return new ProcessCancelResult(ProcessCancelOutcome.CallInFlight, inFlight);
        }

        var revision = await CancelInstanceAsync(transaction, instanceId, cancellationToken)
            ?? throw new InvalidOperationException($"The locked instance '{instanceId}' could not be cancelled.");
        var taskIds = await CancelTasksAsync(transaction, instanceId, cancellationToken);
        var operationIds = await CancelCallsAsync(transaction, "axis.connector_calls", instanceId, cancellationToken);
        operationIds.AddRange(await CancelCallsAsync(transaction, "axis.package_calls", instanceId, cancellationToken));
        await DeleteWorkItemsAsync(transaction, instanceId, cancellationToken);

        var startedAt = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(
                instanceId,
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
                instanceId,
                details,
                userName),
            cancellationToken);

        return new ProcessCancelResult(ProcessCancelOutcome.Cancelled, ReleaseId: instance.ReleaseId, SubjectId: instance.SubjectId);
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
            SELECT application_id, process_id, subject_entity_id, subject_id, release_id, state, step
            FROM axis.process_instances WHERE id = @id FOR UPDATE
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new LockedInstance(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetString(6))
            : null;
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

    private sealed record LockedInstance(
        Guid ApplicationId,
        Guid ProcessId,
        Guid SubjectEntityId,
        Guid SubjectId,
        Guid ReleaseId,
        string State,
        string Step);
}
