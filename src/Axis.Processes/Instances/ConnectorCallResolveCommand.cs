using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Records;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;
using Npgsql;
using Messages = Axis.Data.Records.RecordInputMessages;

namespace Axis.Processes.Instances;

/// <summary>How a resolve of a connector call ended.</summary>
public enum ConnectorCallResolveOutcome
{
    /// <summary>The call is settled and its instance moved on or failed. The caller commits.</summary>
    Resolved,

    /// <summary>The instance does not exist for the process, or its subject record is not readable by the user.</summary>
    NotFound,

    /// <summary>The call does not exist, or belongs to another instance.</summary>
    CallNotFound,

    /// <summary>
    /// A <c>read</c> filter of the user reads a computed field that is being recomputed, so no instance can be
    /// checked against it. Nothing was written.
    /// </summary>
    PendingFilter,

    /// <summary>The call is not <c>unknown</c>.</summary>
    NotUnknown,

    /// <summary>The instance is not <c>waiting</c> on the call's step.</summary>
    NotWaiting,

    /// <summary>The output does not fit the operation's declared outputs, or the record refuses the write, see <see cref="ConnectorCallResolveResult.Errors"/>.</summary>
    Invalid,
}

/// <summary>
/// The result of a resolve. <see cref="ReleaseId"/>, <see cref="SubjectId"/> and <see cref="State"/> are set for
/// <see cref="ConnectorCallResolveOutcome.Resolved"/>, and <see cref="Errors"/>, keyed by JSON Pointer into the
/// body, for <see cref="ConnectorCallResolveOutcome.Invalid"/>.
/// </summary>
public sealed record ConnectorCallResolveResult(
    ConnectorCallResolveOutcome Outcome,
    Guid? ReleaseId = null,
    Guid? SubjectId = null,
    string? State = null,
    SortedDictionary<string, string[]>? Errors = null);

/// <summary>
/// Settles an <c>unknown</c> connector call by hand, in the caller's transaction. The user's <c>resolve</c> grant
/// and the application's shared start lock are the caller's. The instance must be readable by the user, or it
/// does not exist for them, and a <c>read</c> filter that reads a field being recomputed refuses the command
/// first, as for a retry. The call must belong to the instance, be <c>unknown</c>, and the instance must be
/// <c>waiting</c> on the call's step. The call's pinned release says which connector operation it is.
/// Resolved as succeeded, <c>output</c> is checked against the operation's declared outputs as a response is, and
/// each output the step maps is written to the subject record with the record update rules on the pinned release.
/// The instance then runs on to the step's <c>next</c> with a <c>process.step</c> work item, and the data change
/// triggers of the update start. Resolved as failed, the instance is <c>failed</c> and a retry stages the call
/// again under its operation identity. Either way the call keeps its last status and last error, its work item is
/// deleted, a history row marked <c>resolved</c> and the audit records <c>connector.callResolved</c> and, for the
/// write or the failure, <c>record.updated</c> or <c>process.failed</c> are written, all with the user as actor.
/// No <c>connector.called</c> is written, because nothing was sent. The command never commits or rolls back.
/// A result other than <see cref="ConnectorCallResolveOutcome.Resolved"/> may have locked rows, so the caller rolls back.
/// </summary>
public static class ConnectorCallResolveCommand
{
    private const string OutputPointer = "/output";

    private const string ValuesPointer = "/values/";

    /// <param name="active">The active release's model, which the user's read filters are evaluated in.</param>
    /// <param name="activeReleaseId">The active release's id, which the data change triggers are pinned to.</param>
    /// <param name="process">The process of the route. The instance must belong to it.</param>
    /// <param name="entity">The subject entity of <paramref name="process"/> in <paramref name="active"/>.</param>
    /// <param name="read">The user's <c>read</c> access to <paramref name="entity"/>.</param>
    /// <param name="succeeded"><see langword="true"/> to settle the call as succeeded, <see langword="false"/> as failed.</param>
    /// <param name="output">The outputs of a succeeded call, keyed by the declared output names, or <see langword="null"/> for none.</param>
    /// <param name="releaseOf">Gives the model of the release a call is pinned to.</param>
    public static async Task<ConnectorCallResolveResult> ResolveAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel active,
        Guid activeReleaseId,
        ProcessModel process,
        EntityModel entity,
        Guid instanceId,
        Guid callId,
        RecordAccess read,
        string userId,
        string userName,
        string reason,
        bool succeeded,
        JsonObject? output,
        Func<Guid, CancellationToken, Task<ApplicationModel>> releaseOf,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(releaseOf);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // A read filter on a field that is being recomputed decides nothing. It is checked before the instance
        // is found, so it tells nothing about the instance.
        if (read.Filters is { } pendingFilters
            && await RecordCommand.ReadsPendingAsync(connection, transaction, active, entity, pendingFilters, loggerFactory, cancellationToken))
        {
            return new ConnectorCallResolveResult(ConnectorCallResolveOutcome.PendingFilter);
        }

        var instance = await LockInstanceAsync(transaction, instanceId, cancellationToken);
        if (instance is null || instance.ApplicationId != active.Manifest.Id || instance.ProcessId != process.Id)
        {
            return new ConnectorCallResolveResult(ConnectorCallResolveOutcome.NotFound);
        }

        if (!read.Granted
            || (read.Filters is { } filters
                && await RecordCommand.MatchAsync(connection, transaction, active, entity, instance.SubjectId, filters, userId, loggerFactory, cancellationToken)
                    is not { Found: true, Matched: true }))
        {
            return new ConnectorCallResolveResult(ConnectorCallResolveOutcome.NotFound);
        }

        var call = await ConnectorCalls.LoadAsync(transaction, callId, cancellationToken);
        if (call is null || call.ProcessInstanceId != instanceId)
        {
            return new ConnectorCallResolveResult(ConnectorCallResolveOutcome.CallNotFound);
        }

        if (call.State != ConnectorCalls.Unknown)
        {
            return new ConnectorCallResolveResult(ConnectorCallResolveOutcome.NotUnknown);
        }

        if (instance.State != ProcessStarts.Waiting || instance.Step != call.Step)
        {
            return new ConnectorCallResolveResult(ConnectorCallResolveOutcome.NotWaiting);
        }

        var pinned = await releaseOf(call.ReleaseId, cancellationToken);
        var (step, operation, subject) = FindTarget(pinned, instance, call);
        List<KeyValuePair<FieldModel, object?>> values = [];
        if (succeeded)
        {
            var (fitted, errors) = Fit(output, step, operation, subject);
            if (errors is not null)
            {
                return new ConnectorCallResolveResult(ConnectorCallResolveOutcome.Invalid, Errors: errors);
            }

            values = fitted;
        }

        // The call has no lookup that could still settle it, but a lookup that is committing holds its work item.
        await DeleteWorkItemAsync(transaction, callId, cancellationToken);

        var details = new JsonObject
        {
            ["step"] = call.Step,
            ["connector"] = call.Connector,
            ["operation"] = call.Operation,
            ["operationId"] = call.Id,
            ["outcome"] = succeeded ? ConnectorCalls.Succeeded : ConnectorCalls.Failed,
            ["reason"] = reason,
        };
        var result = succeeded
            ? await SucceedAsync(connection, transaction, tenantId, active, activeReleaseId, pinned, subject, step, instance, call, values, userId, userName, loggerFactory, cancellationToken)
            : await FailAsync(connection, transaction, instance, call, userId, userName, cancellationToken);
        if (result.Outcome != ConnectorCallResolveOutcome.Resolved)
        {
            return result;
        }

        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                userId,
                AuditActions.ConnectorCallResolved,
                instance.ApplicationId,
                instance.SubjectEntityId,
                instance.SubjectId,
                instanceId,
                details,
                userName),
            cancellationToken);
        return result with { ReleaseId = instance.ReleaseId, SubjectId = instance.SubjectId };
    }

    private static async Task<ConnectorCallResolveResult> SucceedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel active,
        Guid activeReleaseId,
        ApplicationModel pinned,
        EntityModel subject,
        CallConnectorStepModel step,
        LockedInstance instance,
        LoadedConnectorCall call,
        List<KeyValuePair<FieldModel, object?>> values,
        string userId,
        string userName,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        Record? updated = null;
        if (values.Count > 0)
        {
            // The caller holds the start lock. The row lock makes the read below see the latest version.
            await RecordCommands.LockAsync(connection, subject, instance.SubjectId, cancellationToken);
        }

        var record = await RecordQueries.GetAsync(connection, pinned, subject, instance.SubjectId, cancellationToken);
        var output = new JsonObject
        {
            ["operationId"] = call.Id,
            ["outcome"] = ConnectorCalls.Succeeded,
            ["next"] = step.Next,
            ["resolved"] = true,
        };
        AuditEntry? recordAudit = null;
        if (values.Count > 0)
        {
            if (record is null)
            {
                return Invalid(OutputPointer, $"No record of '{subject.Name}' has the id '{instance.SubjectId}'.");
            }

            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            var written = await RecordCommands.SetAsync(connection, pinned, subject, call.ReleaseId, record, values, now, cancellationToken);
            if (written is not { Outcome: RecordWriteOutcome.Written, Record: { } stored })
            {
                return new ConnectorCallResolveResult(ConnectorCallResolveOutcome.Invalid, Errors: Remap(written, step));
            }

            await ComputedBackfill.MarkStaleWriteAsync(connection, transaction, tenantId, pinned, subject, call.ReleaseId, cancellationToken);
            updated = stored;
            output["subjectVersion"] = stored.Version;
            recordAudit = new AuditEntry(
                userId,
                AuditActions.RecordUpdated,
                instance.ApplicationId,
                instance.SubjectEntityId,
                instance.SubjectId,
                instance.Id,
                new JsonObject
                {
                    ["version"] = stored.Version,
                    ["fields"] = new JsonArray([.. values.Select(value => JsonValue.Create(value.Key.Name))]),
                },
                userName);
        }

        if (!await ProcessSteps.TryResumeCallAsync(transaction, instance.Id, call.Step, instance.Revision, step.Next, cancellationToken))
        {
            throw new InvalidOperationException($"The process instance '{instance.Id}' is no longer waiting on the step '{call.Step}'.");
        }

        await ConnectorCalls.ResolveAsync(transaction, call.Id, ConnectorCalls.Succeeded, cancellationToken);
        var startedAt = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(
                instance.Id,
                call.Step,
                instance.Revision,
                new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = record?.Version },
                output,
                Decision: null,
                Error: null,
                startedAt),
            cancellationToken);
        if (recordAudit is not null)
        {
            await AuditRecords.AppendAsync(transaction, recordAudit, cancellationToken);
        }

        if (record is not null && updated is not null)
        {
            await DataChangeTriggerStarts.StartAfterUpdateAsync(
                transaction,
                tenantId,
                active,
                activeReleaseId,
                call.ReleaseId,
                subject,
                record,
                updated,
                [],
                loggerFactory.CreateLogger(typeof(DataChangeTriggerStarts)),
                cancellationToken);
        }

        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            tenantId,
            ProcessStarts.StepWorkItemKind,
            DateTimeOffset.UtcNow,
            cancellationToken,
            processInstanceId: instance.Id);
        return new ConnectorCallResolveResult(ConnectorCallResolveOutcome.Resolved, State: ProcessStarts.Running);
    }

    private static async Task<ConnectorCallResolveResult> FailAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LockedInstance instance,
        LoadedConnectorCall call,
        string userId,
        string userName,
        CancellationToken cancellationToken)
    {
        if (!await ProcessSteps.TryFailWaitingAsync(transaction, instance.Id, call.Step, instance.Revision, cancellationToken))
        {
            throw new InvalidOperationException($"The process instance '{instance.Id}' is no longer waiting on the step '{call.Step}'.");
        }

        await ConnectorCalls.ResolveAsync(transaction, call.Id, ConnectorCalls.Failed, cancellationToken);
        var startedAt = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(
                instance.Id,
                call.Step,
                instance.Revision,
                new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = null },
                new JsonObject { ["operationId"] = call.Id, ["outcome"] = ConnectorCalls.Failed, ["resolved"] = true },
                Decision: null,
                Error: "An operator resolved the call as failed.",
                startedAt),
            cancellationToken);
        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                userId,
                AuditActions.ProcessFailed,
                instance.ApplicationId,
                instance.SubjectEntityId,
                instance.SubjectId,
                instance.Id,
                new JsonObject { ["step"] = call.Step },
                userName),
            cancellationToken);
        return new ConnectorCallResolveResult(ConnectorCallResolveOutcome.Resolved, State: ProcessStarts.Failed);
    }

    /// <summary>Finds the step, the operation and the subject entity of the call on its pinned release.</summary>
    /// <exception cref="InvalidOperationException">The release lacks one of them.</exception>
    private static (CallConnectorStepModel Step, ConnectorOperationModel Operation, EntityModel Subject) FindTarget(
        ApplicationModel pinned,
        LockedInstance instance,
        LoadedConnectorCall call)
    {
        var process = pinned.Processes.FirstOrDefault(candidate => candidate.Id == instance.ProcessId)
            ?? throw new InvalidOperationException($"The release has no process '{instance.ProcessId}'.");
        var subject = pinned.FindEntity(instance.SubjectEntityId)
            ?? throw new InvalidOperationException($"The release has no entity '{instance.SubjectEntityId}'.");
        if (!process.TryGetStep(call.Step, out var found) || found is not CallConnectorStepModel step)
        {
            throw new InvalidOperationException($"The process '{process.Name}' has no callConnector step '{call.Step}'.");
        }

        var connector = pinned.Connectors.FirstOrDefault(candidate => string.Equals(candidate.Name, call.Connector, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The release has no connector '{call.Connector}'.");
        var operation = connector.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, call.Operation, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The connector '{connector.Name}' has no operation '{call.Operation}'.");
        return (step, operation, subject);
    }

    /// <summary>
    /// Checks <paramref name="output"/> against the declared outputs as a response is: no undeclared name, a
    /// value of the right type for each output it holds, and a value for each required output. Returns the
    /// subject fields to write with their values, or every problem keyed <c>/output/&lt;name&gt;</c>. An optional
    /// output left out writes nothing, and an explicit <c>null</c> writes <c>null</c>.
    /// </summary>
    private static (List<KeyValuePair<FieldModel, object?>> Values, SortedDictionary<string, string[]>? Errors) Fit(
        JsonObject? output,
        CallConnectorStepModel step,
        ConnectorOperationModel operation,
        EntityModel subject)
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var declared = operation.Output.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var property in output ?? [])
        {
            if (!declared.Contains(property.Key))
            {
                errors[Pointer(property.Key)] = [Messages.UnknownProperty];
            }
        }

        var values = new List<KeyValuePair<FieldModel, object?>>();
        foreach (var declaredOutput in operation.Output)
        {
            var present = output is not null && output.TryGetPropertyValue(declaredOutput.Name, out _);
            var node = present ? output![declaredOutput.Name] : null;
            object? value = null;
            if (node is null)
            {
                if (declaredOutput.Required)
                {
                    errors[Pointer(declaredOutput.Name)] = [Messages.Required];
                    continue;
                }
            }
            else if (!ConnectorCallHandler.TryRead(JsonSerializer.SerializeToElement(node), declaredOutput.Type, out value))
            {
                errors[Pointer(declaredOutput.Name)] = [TypeMessage(declaredOutput.Type)];
                continue;
            }

            var mapping = step.Output.FirstOrDefault(candidate => string.Equals(candidate.Output, declaredOutput.Name, StringComparison.OrdinalIgnoreCase));
            if (present && mapping is not null)
            {
                if (!subject.TryGetField(mapping.Field, out var field))
                {
                    throw new InvalidOperationException($"The entity '{subject.Name}' has no field '{mapping.Field}'.");
                }

                values.Add(KeyValuePair.Create(field, value));
            }
        }

        return (values, errors.Count > 0 ? errors : null);
    }

    // Each /values/<field> key of a refused record write is the output that maps to the field.
    private static SortedDictionary<string, string[]> Remap(RecordWriteResult result, CallConnectorStepModel step)
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (key, messages) in result.Errors ?? [])
        {
            var output = key.StartsWith(ValuesPointer, StringComparison.Ordinal)
                ? step.Output.FirstOrDefault(candidate => string.Equals(candidate.Field, key[ValuesPointer.Length..], StringComparison.OrdinalIgnoreCase))
                : null;
            var pointer = output is null ? OutputPointer : Pointer(output.Output);
            errors[pointer] = errors.TryGetValue(pointer, out var existing) ? [.. existing, .. messages] : messages;
        }

        if (errors.Count == 0)
        {
            errors[OutputPointer] = [result.Outcome.ToString()];
        }

        return errors;
    }

    private static ConnectorCallResolveResult Invalid(string pointer, string message) =>
        new(ConnectorCallResolveOutcome.Invalid, Errors: new SortedDictionary<string, string[]>(StringComparer.Ordinal) { [pointer] = [message] });

    private static string Pointer(string name) =>
        $"{OutputPointer}/{name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}";

    private static string TypeMessage(FieldType type) =>
        type switch
        {
            FieldType.Text => Messages.Text,
            FieldType.Integer => Messages.Integer,
            FieldType.Decimal => Messages.Decimal,
            FieldType.Boolean => Messages.Boolean,
            FieldType.Date => Messages.Date,
            FieldType.DateTime => Messages.DateTime,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "A connector output has a scalar type."),
        };

    private static async Task<LockedInstance?> LockInstanceAsync(NpgsqlTransaction transaction, Guid instanceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step
            FROM axis.process_instances WHERE id = @id FOR UPDATE
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new LockedInstance(
                instanceId,
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.GetString(7))
            : null;
    }

    private static async Task DeleteWorkItemAsync(NpgsqlTransaction transaction, Guid callId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "DELETE FROM axis.process_work_items WHERE connector_call_id = @call",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("call", callId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlConnection Connection(NpgsqlTransaction transaction) =>
        transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));

    private sealed record LockedInstance(
        Guid Id,
        Guid ApplicationId,
        Guid ProcessId,
        Guid SubjectEntityId,
        Guid SubjectId,
        Guid ReleaseId,
        string State,
        long Revision,
        string Step);
}
