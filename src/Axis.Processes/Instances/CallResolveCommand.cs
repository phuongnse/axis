using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Records;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;
using Npgsql;
using Messages = Axis.Data.Records.RecordInputMessages;

namespace Axis.Processes.Instances;

/// <summary>How a resolve of a connector or package call ended.</summary>
public enum CallResolveOutcome
{
    /// <summary>The call is settled and its instance moved on or failed. The caller commits.</summary>
    Resolved,

    /// <summary>
    /// The instance does not exist for the process, or its subject record is not readable by the user. An
    /// instance without a subject has no record to read.
    /// </summary>
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

    /// <summary>The output does not fit the operation's declared outputs, or the record refuses the write, see <see cref="CallResolveResult.Errors"/>.</summary>
    Invalid,
}

/// <summary>
/// The result of a resolve. <see cref="ReleaseId"/>, <see cref="SubjectId"/> and <see cref="State"/> are set for
/// <see cref="CallResolveOutcome.Resolved"/>, and <see cref="Errors"/>, keyed by JSON Pointer into the
/// body, for <see cref="CallResolveOutcome.Invalid"/>.
/// </summary>
public sealed record CallResolveResult(
    CallResolveOutcome Outcome,
    Guid? ReleaseId = null,
    Guid? SubjectId = null,
    string? State = null,
    SortedDictionary<string, string[]>? Errors = null);

/// <summary>
/// Settles an <c>unknown</c> connector or package call by hand, in the caller's transaction. The user's <c>resolve</c> grant
/// and the application's shared start lock are the caller's. The instance's subject record must be readable by the user, or it
/// does not exist for them. An instance without a subject has no record, so the grant is the only check and
/// nothing is written to a record. A <c>read</c> filter that reads a field being recomputed refuses the command
/// first, as for a retry. The call must belong to the instance, be <c>unknown</c>, and the instance must be
/// <c>waiting</c> on the call's step. The command looks for a connector call first, then a package call. The
/// call's pinned release says which connector operation it is, and for a package call it says which package
/// version, whose manifest declares the operation's outputs.
/// Resolved as succeeded, <c>output</c> is checked against the operation's declared outputs as a response is, and
/// each output the step maps is written to the subject record with the record update rules on the pinned release.
/// The instance then runs on to the step's <c>next</c> with a <c>process.step</c> work item, and the data change
/// triggers of the update start. Resolved as failed, the instance is <c>failed</c> and a retry stages the call
/// again under its operation identity. Either way the call keeps its last status and last error, its work item is
/// deleted, a history row marked <c>resolved</c> and the audit records <c>connector.callResolved</c> or
/// <c>operation.callResolved</c> and, for the write or the failure, <c>record.updated</c> or <c>process.failed</c>
/// are written, all with the user as actor. A package call also has its stored input cleared. No
/// <c>connector.called</c> or <c>operation.called</c> is written, because nothing was sent or run. The command never commits or rolls back.
/// A result other than <see cref="CallResolveOutcome.Resolved"/> may have locked rows, so the caller rolls back.
/// </summary>
public static class CallResolveCommand
{
    private const string OutputPointer = "/output";

    private const string ValuesPointer = "/values/";

    /// <param name="active">The active release's model, which the user's read filters are evaluated in.</param>
    /// <param name="activeReleaseId">The active release's id, which the data change triggers are pinned to.</param>
    /// <param name="process">The process of the route. The instance must belong to it.</param>
    /// <param name="entity">The subject entity of <paramref name="process"/> in <paramref name="active"/>, or null for a process without a subject.</param>
    /// <param name="read">The user's <c>read</c> access to <paramref name="entity"/>, or null for a process without a subject.</param>
    /// <param name="succeeded"><see langword="true"/> to settle the call as succeeded, <see langword="false"/> as failed.</param>
    /// <param name="output">The outputs of a succeeded call, keyed by the declared output names, or <see langword="null"/> for none.</param>
    /// <param name="releaseOf">Gives the model of the release a call is pinned to.</param>
    /// <param name="packages">The installed packages, which declare the outputs of a package operation.</param>
    public static async Task<CallResolveResult> ResolveAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel active,
        Guid activeReleaseId,
        ProcessModel process,
        EntityModel? entity,
        Guid instanceId,
        Guid callId,
        RecordAccess? read,
        string userId,
        string reason,
        bool succeeded,
        JsonObject? output,
        Func<Guid, CancellationToken, Task<ApplicationModel>> releaseOf,
        PackageCatalog packages,
        RecordCipher? cipher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(releaseOf);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // A read filter on a field that is being recomputed decides nothing. It is checked before the instance
        // is found, so it tells nothing about the instance.
        if (entity is not null
            && read?.Filters is { } pendingFilters
            && await RecordCommand.ReadsPendingAsync(connection, transaction, active, entity, pendingFilters, loggerFactory, cancellationToken))
        {
            return new CallResolveResult(CallResolveOutcome.PendingFilter);
        }

        var instance = await LockInstanceAsync(transaction, instanceId, cancellationToken);
        if (instance is null || instance.ApplicationId != active.Manifest.Id || instance.ProcessId != process.Id)
        {
            return new CallResolveResult(CallResolveOutcome.NotFound);
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
            return new CallResolveResult(CallResolveOutcome.NotFound);
        }

        var (target, refusal) = await FindCallAsync(transaction, instance, callId, releaseOf, packages, cancellationToken);
        if (target is null)
        {
            return new CallResolveResult(refusal!.Value);
        }

        List<KeyValuePair<FieldModel, object?>> values = [];
        if (succeeded)
        {
            var (fitted, errors) = Fit(output, target.Declared, target.Mappings, target.Subject);
            if (errors is not null)
            {
                return new CallResolveResult(CallResolveOutcome.Invalid, Errors: errors);
            }

            values = fitted;
        }

        // The call has no lookup that could still settle it, but a lookup that is committing holds its work item.
        await DeleteWorkItemAsync(transaction, target.WorkItemColumn, callId, cancellationToken);

        var details = new JsonObject
        {
            ["step"] = target.Step,
            [target.OwnerKey] = target.Owner,
            ["operation"] = target.Operation,
            ["operationId"] = target.Id,
            ["outcome"] = succeeded ? ConnectorCalls.Succeeded : ConnectorCalls.Failed,
            ["reason"] = reason,
        };
        var result = succeeded
            ? await SucceedAsync(connection, transaction, tenantId, active, activeReleaseId, target, instance, values, userId, cipher, loggerFactory, cancellationToken)
            : await FailAsync(connection, transaction, target, instance, userId, cancellationToken);
        if (result.Outcome != CallResolveOutcome.Resolved)
        {
            return result;
        }

        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                userId,
                target.AuditAction,
                instance.ApplicationId,
                instance.SubjectEntityId,
                instance.SubjectId,
                instanceId,
                details),
            cancellationToken);
        return result with { ReleaseId = instance.ReleaseId, SubjectId = instance.SubjectId };
    }

    // A connector call first, then a package call. The target is null, with the reason, when neither exists for
    // the instance, or the call is not unknown, or the instance is not waiting on its step. The pinned release
    // is read only for a call that can be resolved.
    private static async Task<(CallTarget? Target, CallResolveOutcome? Refusal)> FindCallAsync(
        NpgsqlTransaction transaction,
        LockedInstance instance,
        Guid callId,
        Func<Guid, CancellationToken, Task<ApplicationModel>> releaseOf,
        PackageCatalog packages,
        CancellationToken cancellationToken)
    {
        if (await ConnectorCalls.LoadAsync(transaction, callId, cancellationToken) is { } connectorCall)
        {
            if (Refuse(instance, connectorCall.ProcessInstanceId, connectorCall.State == ConnectorCalls.Unknown, connectorCall.Step) is { } connectorRefusal)
            {
                return (null, connectorRefusal);
            }

            var pinned = await releaseOf(connectorCall.ReleaseId, cancellationToken);
            var (step, operation, subject) = FindConnectorTarget(pinned, instance, connectorCall);
            return (new CallTarget(
                connectorCall.Id,
                connectorCall.Step,
                connectorCall.ReleaseId,
                "connector",
                connectorCall.Connector,
                connectorCall.Operation,
                step.Next,
                step.Output,
                [.. operation.Output.Select(field => (field.Name, field.Type, field.Required))],
                pinned,
                subject,
                "connector_call_id",
                AuditActions.ConnectorCallResolved,
                (state, token) => ConnectorCalls.ResolveAsync(transaction, connectorCall.Id, state, token)), null);
        }

        if (await PackageCalls.LoadAsync(transaction, callId, cancellationToken) is not { } packageCall)
        {
            return (null, CallResolveOutcome.CallNotFound);
        }

        if (Refuse(instance, packageCall.ProcessInstanceId, packageCall.State == PackageCalls.Unknown, packageCall.Step) is { } packageRefusal)
        {
            return (null, packageRefusal);
        }

        var packagePinned = await releaseOf(packageCall.ReleaseId, cancellationToken);
        var (packageStep, packageOperation, packageSubject) = FindPackageTarget(packagePinned, packages, instance, packageCall);
        return (new CallTarget(
            packageCall.Id,
            packageCall.Step,
            packageCall.ReleaseId,
            "package",
            packageCall.Package,
            packageCall.Operation,
            packageStep.Next,
            packageStep.Output,
            [.. packageOperation.Output.Select(field => (field.Name, field.Type, field.Required))],
            packagePinned,
            packageSubject,
            "package_call_id",
            AuditActions.OperationCallResolved,
            (state, token) => PackageCalls.ResolveAsync(transaction, packageCall.Id, state, token)), null);
    }

    private static CallResolveOutcome? Refuse(LockedInstance instance, Guid callInstanceId, bool unknown, string step)
    {
        if (callInstanceId != instance.Id)
        {
            return CallResolveOutcome.CallNotFound;
        }

        if (!unknown)
        {
            return CallResolveOutcome.NotUnknown;
        }

        return instance.State != ProcessStarts.Waiting || instance.Step != step ? CallResolveOutcome.NotWaiting : null;
    }

    private static async Task<CallResolveResult> SucceedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel active,
        Guid activeReleaseId,
        CallTarget target,
        LockedInstance instance,
        List<KeyValuePair<FieldModel, object?>> values,
        string userId,
        RecordCipher? cipher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var (pinned, subject) = (target.Pinned, target.Subject);
        Record? updated = null;

        // An instance without a subject has no record to read or write. A step of such a process maps no
        // outputs, so there are no values either.
        Record? record = null;
        if (subject is not null && instance.SubjectId is { } subjectId)
        {
            if (values.Count > 0)
            {
                // The caller holds the start lock. The row lock makes the read below see the latest version.
                await RecordCommands.LockAsync(connection, subject, subjectId, cancellationToken);
            }

            record = await RecordQueries.GetAsync(connection, pinned, subject, subjectId, cipher, cancellationToken);
        }

        var output = new JsonObject
        {
            ["operationId"] = target.Id,
            ["outcome"] = ConnectorCalls.Succeeded,
            ["next"] = target.Next,
            ["resolved"] = true,
        };
        AuditEntry? recordAudit = null;
        if (values.Count > 0)
        {
            if (subject is null || record is null)
            {
                return Invalid(OutputPointer, $"No record of '{subject?.Name}' has the id '{instance.SubjectId}'.");
            }

            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            var written = await RecordCommands.SetAsync(connection, pinned, subject, target.ReleaseId, record, values, now, cipher: cipher, cancellationToken: cancellationToken);
            if (written is not { Outcome: RecordWriteOutcome.Written, Record: { } stored })
            {
                return new CallResolveResult(CallResolveOutcome.Invalid, Errors: Remap(written, target.Mappings));
            }

            await ComputedBackfill.MarkStaleWriteAsync(connection, transaction, tenantId, pinned, subject, target.ReleaseId, cancellationToken);
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
                });
        }

        if (!await ProcessSteps.TryResumeCallAsync(transaction, instance.Id, target.Step, instance.Revision, target.Next, cancellationToken))
        {
            throw new InvalidOperationException($"The process instance '{instance.Id}' is no longer waiting on the step '{target.Step}'.");
        }

        await target.SettleAsync(ConnectorCalls.Succeeded, cancellationToken);
        var startedAt = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(
                instance.Id,
                target.Step,
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

        if (subject is not null && record is not null && updated is not null)
        {
            await DataChangeTriggerStarts.StartAfterUpdateAsync(
                transaction,
                tenantId,
                active,
                activeReleaseId,
                target.ReleaseId,
                subject,
                record,
                updated,
                [],
                userId,
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
        return new CallResolveResult(CallResolveOutcome.Resolved, State: ProcessStarts.Running);
    }

    private static async Task<CallResolveResult> FailAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CallTarget target,
        LockedInstance instance,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!await ProcessSteps.TryFailWaitingAsync(transaction, instance.Id, target.Step, instance.Revision, cancellationToken))
        {
            throw new InvalidOperationException($"The process instance '{instance.Id}' is no longer waiting on the step '{target.Step}'.");
        }

        await target.SettleAsync(ConnectorCalls.Failed, cancellationToken);
        var startedAt = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(
                instance.Id,
                target.Step,
                instance.Revision,
                new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = null },
                new JsonObject { ["operationId"] = target.Id, ["outcome"] = ConnectorCalls.Failed, ["resolved"] = true },
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
                new JsonObject { ["step"] = target.Step }),
            cancellationToken);
        return new CallResolveResult(CallResolveOutcome.Resolved, State: ProcessStarts.Failed);
    }

    /// <summary>Finds the step, the operation and the subject entity, null without a subject, of the call on its pinned release.</summary>
    /// <exception cref="InvalidOperationException">The release lacks one of them.</exception>
    private static (CallConnectorStepModel Step, ConnectorOperationModel Operation, EntityModel? Subject) FindConnectorTarget(
        ApplicationModel pinned,
        LockedInstance instance,
        LoadedConnectorCall call)
    {
        var process = pinned.Processes.FirstOrDefault(candidate => candidate.Id == instance.ProcessId)
            ?? throw new InvalidOperationException($"The release has no process '{instance.ProcessId}'.");
        var subject = instance.SubjectEntityId is { } subjectEntityId
            ? pinned.FindEntity(subjectEntityId) ?? throw new InvalidOperationException($"The release has no entity '{subjectEntityId}'.")
            : null;
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

    /// <summary>Finds the step, the manifest's operation and the subject entity, null without a subject, of the package call.</summary>
    /// <exception cref="InvalidOperationException">The release lacks the step, or the catalogue lacks the package version or its operation.</exception>
    private static (PackageOperationStepModel Step, PackageOperation Operation, EntityModel? Subject) FindPackageTarget(
        ApplicationModel pinned,
        PackageCatalog packages,
        LockedInstance instance,
        LoadedPackageCall call)
    {
        var process = pinned.Processes.FirstOrDefault(candidate => candidate.Id == instance.ProcessId)
            ?? throw new InvalidOperationException($"The release has no process '{instance.ProcessId}'.");
        var subject = instance.SubjectEntityId is { } subjectEntityId
            ? pinned.FindEntity(subjectEntityId) ?? throw new InvalidOperationException($"The release has no entity '{subjectEntityId}'.")
            : null;
        if (!process.TryGetStep(call.Step, out var found) || found is not PackageOperationStepModel step)
        {
            throw new InvalidOperationException($"The process '{process.Name}' has no package operation step '{call.Step}'.");
        }

        var package = packages.Find(call.Package, call.Version)
            ?? throw new InvalidOperationException($"The package '{call.Package}' {call.Version} is not installed on this server.");
        var operation = package.Manifest.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, call.Operation, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The package '{call.Package}' {call.Version} has no operation '{call.Operation}'.");
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
        IReadOnlyList<(string Name, FieldType Type, bool Required)> declared,
        IReadOnlyList<ConnectorOutputModel> mappings,
        EntityModel? subject)
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var declaredNames = declared.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var property in output ?? [])
        {
            if (!declaredNames.Contains(property.Key))
            {
                errors[Pointer(property.Key)] = [Messages.UnknownProperty];
            }
        }

        var values = new List<KeyValuePair<FieldModel, object?>>();
        foreach (var declaredOutput in declared)
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

            var mapping = mappings.FirstOrDefault(candidate => string.Equals(candidate.Output, declaredOutput.Name, StringComparison.OrdinalIgnoreCase));
            if (present && mapping is not null)
            {
                // A process without a subject cannot map outputs, which AXC0246 refuses.
                if (subject is null)
                {
                    throw new InvalidOperationException($"The output '{mapping.Output}' is mapped, but the instance has no subject.");
                }

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
    private static SortedDictionary<string, string[]> Remap(RecordWriteResult result, IReadOnlyList<ConnectorOutputModel> mappings)
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (key, messages) in result.Errors ?? [])
        {
            var output = key.StartsWith(ValuesPointer, StringComparison.Ordinal)
                ? mappings.FirstOrDefault(candidate => string.Equals(candidate.Field, key[ValuesPointer.Length..], StringComparison.OrdinalIgnoreCase))
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

    private static CallResolveResult Invalid(string pointer, string message) =>
        new(CallResolveOutcome.Invalid, Errors: new SortedDictionary<string, string[]>(StringComparer.Ordinal) { [pointer] = [message] });

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
            reader.GetInt64(6),
            reader.GetString(7));
    }

    // The column is one of two constants of this class, never text from a request.
    private static async Task DeleteWorkItemAsync(NpgsqlTransaction transaction, string column, Guid callId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"DELETE FROM axis.process_work_items WHERE {column} = @call",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("call", callId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlConnection Connection(NpgsqlTransaction transaction) =>
        transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));

    /// <summary>The call being resolved, connector or package, with what the shared steps need of it.</summary>
    /// <param name="Owner">The connector or package name, under the key <paramref name="OwnerKey"/> in the audit details.</param>
    /// <param name="Settle">Settles the call as <c>succeeded</c> or <c>failed</c>.</param>
    private sealed record CallTarget(
        Guid Id,
        string Step,
        Guid ReleaseId,
        string OwnerKey,
        string Owner,
        string Operation,
        string Next,
        IReadOnlyList<ConnectorOutputModel> Mappings,
        IReadOnlyList<(string Name, FieldType Type, bool Required)> Declared,
        ApplicationModel Pinned,
        EntityModel? Subject,
        string WorkItemColumn,
        string AuditAction,
        Func<string, CancellationToken, Task> Settle)
    {
        public Task SettleAsync(string state, CancellationToken cancellationToken) => Settle(state, cancellationToken);
    }

    private sealed record LockedInstance(
        Guid Id,
        Guid ApplicationId,
        Guid ProcessId,
        Guid? SubjectEntityId,
        Guid? SubjectId,
        Guid ReleaseId,
        string State,
        long Revision,
        string Step);
}
