using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Npgsql;

namespace Axis.Server.Processes;

/// <summary>
/// Starts a process instance on a subject record inside the caller's transaction, as both the
/// process start endpoint and the endpoint call do. It checks the start condition against the
/// record as stored, inserts the running instance pinned to <c>releaseId</c>, queues its first
/// work item and appends the audit record <c>process.started</c>. It never commits or rolls back:
/// the caller does, and rolls back on any result but <see cref="ProcessStartOutcome.Started"/>.
/// </summary>
internal static class ProcessStarter
{
    /// <param name="subject">The subject record as read in this transaction, or null to read it here.</param>
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
        string actorName,
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
                return new ProcessStartResult(ProcessStartOutcome.ConditionFailed, condition.Message.TextKey);
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
        if (!await ProcessStarts.TryInsertInstanceAsync(transaction, instance, cancellationToken))
        {
            return new ProcessStartResult(ProcessStartOutcome.AlreadyRunning);
        }

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

    /// <summary>The problem of a start that found an instance already running or waiting for the record.</summary>
    public static IResult AlreadyRunning() =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "A running or waiting instance of this process already exists for this record.");
}

/// <summary>How a start inside the caller's transaction ended.</summary>
internal enum ProcessStartOutcome
{
    /// <summary>The instance, its first work item and its audit record are written.</summary>
    Started,

    /// <summary>The start condition was not true. <see cref="ProcessStartResult.TextKey"/> holds its message's text key.</summary>
    ConditionFailed,

    /// <summary>An instance of the process is already running or waiting for the record.</summary>
    AlreadyRunning,
}

/// <summary>The outcome of a start, with the start condition's message text key when it failed.</summary>
internal sealed record ProcessStartResult(ProcessStartOutcome Outcome, string? TextKey = null);
