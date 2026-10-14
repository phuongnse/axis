using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Starts a process instance on a subject record inside the caller's transaction, as the process
/// start endpoint, the endpoint call and an event trigger's delivery do. It checks the start
/// condition against the record as stored, inserts the running instance pinned to
/// <c>releaseId</c>, queues its first work item and appends the audit record
/// <c>process.started</c>. The instance insert runs in a savepoint, so a start that finds the
/// process already running leaves the transaction usable. It never commits or rolls back: the
/// caller does, and decides whether a result other than <see cref="ProcessStartOutcome.Started"/>
/// rolls back. A result other than <see cref="ProcessStartOutcome.Started"/> wrote nothing.
/// </summary>
public static class ProcessStarter
{
    private const string StartSavepoint = "start";

    /// <param name="subject">The subject record as read in this transaction, or null to read it here.</param>
    /// <param name="actorName">The actor's display name, or null for <c>system</c>.</param>
    /// <param name="details">More audit details, such as the endpoint's name, added after <c>processId</c> and <c>releaseId</c>.</param>
    public static async Task<ProcessStartResult> StartAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel application,
        Guid releaseId,
        ProcessModel process,
        EntityModel entity,
        Guid subjectId,
        Record? subject,
        Guid instanceId,
        string actorId,
        string? actorName,
        JsonObject? details,
        CancellationToken cancellationToken)
    {
        // Only true passes: false, null and a run-time error all fail the start. now() is this
        // transaction's start time.
        if (process.StartCondition is { } condition)
        {
            var record = subject
                ?? await RecordQueries.GetAsync(connection, application, entity, subjectId, cancellationToken)
                ?? throw new InvalidOperationException("The subject record of a start could not be read in its transaction.");
            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            var result = await RecordExpressions.EvaluateAsync(connection, application, entity, record, condition.Expression, now, cancellationToken);
            if (result.Value is not true)
            {
                return new ProcessStartResult(ProcessStartOutcome.ConditionFailed, condition.Message.TextKey, result.Error?.Message);
            }
        }

        var instance = new ProcessInstanceRow
        {
            Id = instanceId,
            ApplicationId = application.Manifest.Id,
            ProcessId = process.Id,
            SubjectEntityId = entity.Id,
            SubjectId = subjectId,
            ReleaseId = releaseId,
            State = ProcessStarts.Running,
            Revision = 1,
            Step = process.Start,
        };

        // A unique violation aborts the transaction, so the insert runs in a savepoint that the
        // caller's transaction can roll back to.
        await transaction.SaveAsync(StartSavepoint, cancellationToken);
        if (!await ProcessStarts.TryInsertInstanceAsync(transaction, instance, cancellationToken))
        {
            // The rollback keeps the savepoint open, so it is released too. Otherwise the caller's
            // later writes would run in a subtransaction, with an xid of their own.
            await transaction.RollbackAsync(StartSavepoint, cancellationToken);
            await transaction.ReleaseAsync(StartSavepoint, cancellationToken);
            return new ProcessStartResult(ProcessStartOutcome.AlreadyRunning);
        }

        await transaction.ReleaseAsync(StartSavepoint, cancellationToken);

        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            tenantId,
            ProcessStarts.StepWorkItemKind,
            DateTimeOffset.UtcNow,
            cancellationToken,
            processInstanceId: instanceId);

        var auditDetails = new JsonObject { ["processId"] = process.Id, ["releaseId"] = releaseId };
        foreach (var (name, value) in details ?? [])
        {
            auditDetails[name] = value?.DeepClone();
        }

        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(actorId, AuditActions.ProcessStarted, application.Manifest.Id, entity.Id, subjectId, instanceId, auditDetails, actorName),
            cancellationToken);
        return new ProcessStartResult(ProcessStartOutcome.Started);
    }
}

/// <summary>How a start inside the caller's transaction ended.</summary>
public enum ProcessStartOutcome
{
    /// <summary>The instance, its first work item and its audit record are written.</summary>
    Started,

    /// <summary>The start condition was not true. <see cref="ProcessStartResult.TextKey"/> holds its message's text key.</summary>
    ConditionFailed,

    /// <summary>An instance of the process is already running or waiting for the record.</summary>
    AlreadyRunning,
}

/// <summary>
/// The outcome of a start, with the start condition's message text key when it failed, and the
/// run-time error's message when the condition failed with one.
/// </summary>
public sealed record ProcessStartResult(ProcessStartOutcome Outcome, string? TextKey = null, string? Error = null);
