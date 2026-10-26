using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Records;
using Axis.Processes.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>How a retry ended.</summary>
public enum ProcessRetryOutcome
{
    /// <summary>The instance is <c>running</c> again on the step that failed, with a work item. The caller commits.</summary>
    Retried,

    /// <summary>
    /// The instance does not exist for the process, or its subject record is not readable by the user. An
    /// instance without a subject has no record to read.
    /// </summary>
    NotFound,

    /// <summary>The instance is not <c>failed</c>.</summary>
    NotFailed,

    /// <summary>
    /// Another instance of the process is <c>running</c> or <c>waiting</c> for the same record, or for a process
    /// without a subject, at all.
    /// </summary>
    AlreadyRunning,

    /// <summary>
    /// A <c>read</c> filter of the user reads a computed field that is being recomputed, so no instance can be
    /// checked against it. Nothing was written.
    /// </summary>
    PendingFilter,

    /// <summary>
    /// An applied <c>renameField</c>, <c>dropField</c>, <c>dropEntity</c> or <c>convert</c> changed a column that the
    /// instance's pinned release uses, so old steps could run against a changed table. Nothing was written.
    /// </summary>
    MigrationChanged,
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
/// them. An instance without a subject has no record, so the grant is the only check. When a <c>read</c>
/// filter of the user reads a computed field that is being recomputed, the
/// retry is refused first, before the instance is found. The instance keeps its pinned release and its step. It gets the next revision and no end
/// time, and a <c>process.step</c> work item runs the step again with the record as it is then.
/// Another <c>running</c> or <c>waiting</c> instance for the same record, or of the same process when it has no
/// subject, refuses the retry, which
/// the explicit check and the unique index on active instances both catch. A migration applied since the
/// pinned release that changed a column the release uses refuses it too, before anything is written.
/// A pinned release whose stored content is missing counts as using every column. A history row with
/// output <c>{ "command": "retry" }</c> and the audit record <c>process.retried</c> are written.
/// The command never commits or rolls back. A result other than <see cref="ProcessRetryOutcome.Retried"/>
/// may have locked rows, and an <see cref="ProcessRetryOutcome.AlreadyRunning"/> from the unique index
/// leaves the transaction aborted, so the caller rolls back.
/// </summary>
public static class ProcessRetryCommand
{
    /// <param name="active">The active release's model, which the user's read filters are evaluated in.</param>
    /// <param name="process">The process of the route. The instance must belong to it.</param>
    /// <param name="entity">The subject entity of <paramref name="process"/> in <paramref name="active"/>, or null for a process without a subject.</param>
    /// <param name="read">The user's <c>read</c> access to <paramref name="entity"/>, or null for a process without a subject.</param>
    public static async Task<ProcessRetryResult> RetryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel active,
        ProcessModel process,
        EntityModel? entity,
        Guid instanceId,
        RecordAccess? read,
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
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // A read filter on a field that is being recomputed decides nothing. It is checked before the instance
        // is found, so it tells nothing about the instance.
        if (entity is not null
            && read?.Filters is { } pendingFilters
            && await RecordCommand.ReadsPendingAsync(connection, transaction, active, entity, pendingFilters, loggerFactory, cancellationToken))
        {
            return new ProcessRetryResult(ProcessRetryOutcome.PendingFilter);
        }

        var instance = await LockInstanceAsync(transaction, instanceId, cancellationToken);
        if (instance is null || instance.ApplicationId != active.Manifest.Id || instance.ProcessId != process.Id)
        {
            return new ProcessRetryResult(ProcessRetryOutcome.NotFound);
        }

        // An instance without a subject has no record to read, so only the grant applies. An instance with a
        // subject is never exempt from the read check, whatever the process's subject is now.
        if (instance.SubjectId is { } subjectId
            && (entity is null
                || read is not { Granted: true }
                || (read.Filters is { } filters
                    && await RecordCommand.MatchAsync(connection, transaction, active, entity, subjectId, filters, userId, loggerFactory, cancellationToken)
                        is not { Found: true, Matched: true })))
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

        // A failed instance never blocks a destructive migration, so the migrations applied since its release
        // are checked before old steps run again. The start lock of the caller keeps an activation from committing in between.
        if (RetryMigrationCheck.Blocks(
            await AppliedMigrationColumns.ReadAsync(transaction, instance.ApplicationId, cancellationToken),
            await ReadPinnedReleaseAsync(connection, transaction, instance.ReleaseId, cancellationToken)))
        {
            return new ProcessRetryResult(ProcessRetryOutcome.MigrationChanged);
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

    // Null when the stored release is gone.
    private static async Task<Release?> ReadPinnedReleaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid releaseId,
        CancellationToken cancellationToken)
    {
        await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseNpgsql(connection)
            .Options);
        await configuration.Database.UseTransactionAsync(transaction, cancellationToken);
        return await new ActiveReleaseStore(configuration).GetReleaseAsync(releaseId, cancellationToken);
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
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LockedInstance(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6));
    }

    private static async Task<bool> ExistsActiveInstanceAsync(
        NpgsqlTransaction transaction,
        LockedInstance instance,
        Guid instanceId,
        CancellationToken cancellationToken)
    {
        // The subject is compared in two forms, so that the query for a record keeps using the index of active
        // instances for records.
        var subjectPredicate = instance.SubjectId is null ? "subject_id IS NULL" : "subject_id = @subject";
        await using var command = new NpgsqlCommand(
            $"""
            SELECT EXISTS (
                SELECT 1 FROM axis.process_instances
                WHERE application_id = @application AND process_id = @process AND {subjectPredicate}
                    AND state IN (@running, @waiting) AND id <> @id)
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("application", instance.ApplicationId);
        command.Parameters.AddWithValue("process", instance.ProcessId);
        if (instance.SubjectId is { } subject)
        {
            command.Parameters.AddWithValue("subject", subject);
        }

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
        Guid? SubjectEntityId,
        Guid? SubjectId,
        Guid ReleaseId,
        string State,
        string Step);
}
