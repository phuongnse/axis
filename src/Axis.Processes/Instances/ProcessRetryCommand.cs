using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Records;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>How a retry ended.</summary>
public enum ProcessRetryOutcome
{
    /// <summary>The instance is <c>running</c> again on the step that failed, with a work item. The caller commits.</summary>
    Retried,

    /// <summary>The instance does not exist for the process, or its subject record is not readable by the user.</summary>
    NotFound,

    /// <summary>The instance is not <c>failed</c>.</summary>
    NotFailed,

    /// <summary>Another instance of the process is <c>running</c> or <c>waiting</c> for the same record.</summary>
    AlreadyRunning,
}

/// <summary>
/// The result of a retry. <see cref="ReleaseId"/> and <see cref="SubjectId"/> are set for
/// <see cref="ProcessRetryOutcome.Retried"/>.
/// </summary>
public sealed record ProcessRetryResult(ProcessRetryOutcome Outcome, Guid? ReleaseId = null, Guid? SubjectId = null);

/// <summary>
/// Sends a <c>failed</c> instance back to <c>running</c> on the step that failed, in the caller's
/// transaction. The user's <c>retry</c> grant and the application's start lock are the caller's.
/// The instance's subject record must be readable by the user, or the instance does not exist for
/// them. The instance keeps its pinned release and its step. It gets the next revision and no end
/// time, and a <c>process.step</c> work item runs the step again with the record as it is then.
/// Another <c>running</c> or <c>waiting</c> instance for the same record refuses the retry, which
/// the explicit check and the unique index on active instances both catch. A history row with
/// output <c>{ "command": "retry" }</c> and the audit record <c>process.retried</c> are written.
/// The command never commits or rolls back. A result other than <see cref="ProcessRetryOutcome.Retried"/>
/// may have locked rows, and an <see cref="ProcessRetryOutcome.AlreadyRunning"/> from the unique index
/// leaves the transaction aborted, so the caller rolls back.
/// </summary>
public static class ProcessRetryCommand
{
    /// <param name="active">The active release's model, which the user's read filters are evaluated in.</param>
    /// <param name="process">The process of the route. The instance must belong to it.</param>
    /// <param name="entity">The subject entity of <paramref name="process"/> in <paramref name="active"/>.</param>
    /// <param name="read">The user's <c>read</c> access to <paramref name="entity"/>.</param>
    public static async Task<ProcessRetryResult> RetryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
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

        var instance = await LockInstanceAsync(transaction, instanceId, cancellationToken);
        if (instance is null || instance.ApplicationId != active.Manifest.Id || instance.ProcessId != process.Id)
        {
            return new ProcessRetryResult(ProcessRetryOutcome.NotFound);
        }

        if (!read.Granted
            || (read.Filters is { } filters
                && await RecordCommand.MatchAsync(connection, transaction, active, entity, instance.SubjectId, filters, userId, loggerFactory, cancellationToken)
                    is not { Found: true, Matched: true }))
        {
            return new ProcessRetryResult(ProcessRetryOutcome.NotFound);
        }

        if (instance.State != ProcessStarts.Failed)
        {
            return new ProcessRetryResult(ProcessRetryOutcome.NotFailed);
        }

        if (await ExistsActiveInstanceAsync(transaction, instance, instanceId, cancellationToken))
        {
            return new ProcessRetryResult(ProcessRetryOutcome.AlreadyRunning);
        }

        long revision;
        try
        {
            revision = await ReopenInstanceAsync(transaction, instanceId, cancellationToken)
                ?? throw new InvalidOperationException($"The locked instance '{instanceId}' could not be retried.");
        }
        catch (PostgresException exception) when (exception is { SqlState: ProcessStarts.UniqueViolation, ConstraintName: ProcessStarts.ActiveSubjectIndex })
        {
            // A start for the same record committed first.
            return new ProcessRetryResult(ProcessRetryOutcome.AlreadyRunning);
        }

        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            tenantId,
            ProcessStarts.StepWorkItemKind,
            DateTimeOffset.UtcNow,
            cancellationToken,
            processInstanceId: instanceId);

        var startedAt = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(
                instanceId,
                instance.Step,
                revision,
                new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = null },
                new JsonObject { ["command"] = "retry" },
                Decision: null,
                Error: null,
                startedAt),
            cancellationToken);

        var details = new JsonObject
        {
            ["step"] = instance.Step,
            ["reason"] = reason,
        };
        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                userId,
                AuditActions.ProcessRetried,
                instance.ApplicationId,
                instance.SubjectEntityId,
                instance.SubjectId,
                instanceId,
                details,
                userName),
            cancellationToken);

        return new ProcessRetryResult(ProcessRetryOutcome.Retried, ReleaseId: instance.ReleaseId, SubjectId: instance.SubjectId);
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

    private static async Task<bool> ExistsActiveInstanceAsync(
        NpgsqlTransaction transaction,
        LockedInstance instance,
        Guid instanceId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM axis.process_instances
                WHERE application_id = @application AND process_id = @process AND subject_id = @subject
                    AND state IN (@running, @waiting) AND id <> @id)
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("application", instance.ApplicationId);
        command.Parameters.AddWithValue("process", instance.ProcessId);
        command.Parameters.AddWithValue("subject", instance.SubjectId);
        command.Parameters.AddWithValue("running", ProcessStarts.Running);
        command.Parameters.AddWithValue("waiting", ProcessStarts.Waiting);
        command.Parameters.AddWithValue("id", instanceId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    // Returns the revision the instance had before the retry.
    private static async Task<long?> ReopenInstanceAsync(NpgsqlTransaction transaction, Guid instanceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.process_instances
            SET state = @running, revision = revision + 1, ended_at = NULL
            WHERE id = @id AND state = @failed
            RETURNING revision - 1
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", instanceId);
        command.Parameters.AddWithValue("running", ProcessStarts.Running);
        command.Parameters.AddWithValue("failed", ProcessStarts.Failed);
        return await command.ExecuteScalarAsync(cancellationToken) is long revision ? revision : null;
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
