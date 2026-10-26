using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Expressions.Evaluation;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Starts a process instance on a subject record inside the caller's transaction, as the process
/// start endpoint, the endpoint call and an event trigger's delivery do. It checks the start
/// condition against the record the caller passes. A process without a subject starts with
/// <see cref="StartWithParametersAsync"/> instead, which checks the start condition over the
/// parameter values and reads no record. Callers that start on a pending computed field pass
/// the record computed with <see cref="PendingComputedReads"/>. It inserts the running instance pinned to
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
    public static Task<ProcessStartResult> StartAsync(
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
        CancellationToken cancellationToken) =>
        StartOnSubjectAsync(
            connection, transaction, tenantId, application, releaseId, process, entity, subjectId, subject, instanceId, actorId, actorName, details, null, cancellationToken);

    /// <summary>
    /// Starts the instance of a child process for a <c>subProcess</c> step, as <c>system</c> and
    /// pinned to <paramref name="releaseId"/>, the release the parent is pinned to. The start condition
    /// is checked as for any start, and <c>process.started</c> adds <c>parentInstanceId</c>. The caller
    /// holds the application's start lock. A child with a subject passes <paramref name="entity"/>,
    /// <paramref name="subjectId"/> and <paramref name="subject"/> as <see cref="StartAsync"/> takes them. A
    /// child without one passes <paramref name="parameters"/> as <see cref="StartWithParametersAsync"/> does.
    /// </summary>
    /// <param name="parentInstanceId">The instance of the step that starts the child.</param>
    /// <param name="parentStep">The declared name of that step.</param>
    internal static Task<ProcessStartResult> StartChildAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel application,
        Guid releaseId,
        ProcessModel process,
        EntityModel? entity,
        Guid? subjectId,
        Record? subject,
        JsonObject? parameters,
        Guid instanceId,
        Guid parentInstanceId,
        string parentStep,
        CancellationToken cancellationToken)
    {
        var details = new JsonObject { ["parentInstanceId"] = parentInstanceId };
        var parent = (parentInstanceId, parentStep);
        return entity is not null
            ? StartOnSubjectAsync(
                connection, transaction, tenantId, application, releaseId, process, entity, subjectId!.Value, subject, instanceId, AuditActors.System, null, details, parent, cancellationToken)
            : StartOnParametersAsync(
                connection, transaction, tenantId, application, releaseId, process, parameters!, instanceId, AuditActors.System, null, details, parent, cancellationToken);
    }

    private static async Task<ProcessStartResult> StartOnSubjectAsync(
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
        (Guid InstanceId, string Step)? parent,
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
                return new ProcessStartResult(ProcessStartOutcome.ConditionFailed, condition.Message.TextKey, result.Error?.Kind);
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
            ParentInstanceId = parent?.InstanceId,
            ParentStep = parent?.Step,
        };
        return await InsertAsync(connection, transaction, tenantId, application, releaseId, process, instance, actorId, actorName, details, cancellationToken);
    }

    /// <summary>
    /// Starts an instance of a process without a subject, with <paramref name="parameters"/> as its
    /// values in the record API's JSON form. The start condition is checked over the parameter values,
    /// with no record read. The values are stored as they are, so rejecting a missing, unknown or
    /// mistyped parameter is the caller's job. A process without a subject has at most one running
    /// or waiting instance, so a start while one is open is <see cref="ProcessStartOutcome.AlreadyRunning"/>.
    /// </summary>
    /// <param name="actorName">The actor's display name, or null for <c>system</c>.</param>
    /// <param name="details">More audit details, added after <c>processId</c> and <c>releaseId</c>.</param>
    /// <exception cref="ArgumentException">The process has a subject entity.</exception>
    public static Task<ProcessStartResult> StartWithParametersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel application,
        Guid releaseId,
        ProcessModel process,
        JsonObject parameters,
        Guid instanceId,
        string actorId,
        string? actorName,
        JsonObject? details,
        CancellationToken cancellationToken) =>
        StartOnParametersAsync(
            connection, transaction, tenantId, application, releaseId, process, parameters, instanceId, actorId, actorName, details, null, cancellationToken);

    private static async Task<ProcessStartResult> StartOnParametersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel application,
        Guid releaseId,
        ProcessModel process,
        JsonObject parameters,
        Guid instanceId,
        string actorId,
        string? actorName,
        JsonObject? details,
        (Guid InstanceId, string Step)? parent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(parameters);
        if (process.Entity is not null)
        {
            throw new ArgumentException($"The process '{process.Name}' has a subject, so it starts on a record.", nameof(process));
        }

        // Only true passes: false, null and a run-time error all fail the start. now() is this
        // transaction's start time.
        if (process.StartCondition is { } condition)
        {
            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            var result = ProcessParameters.Evaluate(condition.Expression, ProcessParameters.Values(process, parameters), now);
            if (result.Value is not true)
            {
                return new ProcessStartResult(ProcessStartOutcome.ConditionFailed, condition.Message.TextKey, result.Error?.Kind);
            }
        }

        var instance = new ProcessInstanceRow
        {
            Id = instanceId,
            ApplicationId = application.Manifest.Id,
            ProcessId = process.Id,
            ReleaseId = releaseId,
            State = ProcessStarts.Running,
            Revision = 1,
            Step = process.Start,
            Parameters = parameters.ToJsonString(),
            ParentInstanceId = parent?.InstanceId,
            ParentStep = parent?.Step,
        };
        return await InsertAsync(connection, transaction, tenantId, application, releaseId, process, instance, actorId, actorName, details, cancellationToken);
    }

    private static async Task<ProcessStartResult> InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel application,
        Guid releaseId,
        ProcessModel process,
        ProcessInstanceRow instance,
        string actorId,
        string? actorName,
        JsonObject? details,
        CancellationToken cancellationToken)
    {
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
            processInstanceId: instance.Id);

        var auditDetails = new JsonObject { ["processId"] = process.Id, ["releaseId"] = releaseId };
        foreach (var (name, value) in details ?? [])
        {
            auditDetails[name] = value?.DeepClone();
        }

        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(actorId, AuditActions.ProcessStarted, application.Manifest.Id, instance.SubjectEntityId, instance.SubjectId, instance.Id, auditDetails, actorName),
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

    /// <summary>An instance of the process is already running or waiting for the record, or for a process without a subject, at all.</summary>
    AlreadyRunning,
}

/// <summary>
/// The outcome of a start, with the start condition's message text key when it failed, and the
/// run-time error's kind when the condition failed with one. The kind, never the error message, is
/// what a log may name, because the message can quote a value.
/// </summary>
public sealed record ProcessStartResult(ProcessStartOutcome Outcome, string? TextKey = null, ExpressionRuntimeErrorKind? ErrorKind = null);
