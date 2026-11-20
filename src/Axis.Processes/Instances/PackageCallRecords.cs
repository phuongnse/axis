using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Core.Telemetry;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// The writes that record the outcome of a package call, which the worker's <see cref="PackageCallHandler"/> and a customer-hosted
/// runner's result share, see docs/reference/connector-and-package-calls.md#staging-a-package-call. A fitting output is written to the subject record
/// with the record update rules, the call is settled <c>succeeded</c>, the instance runs on to <c>next</c>, and the history row, the attempt row,
/// the audit records, the data change triggers and the next step's work item follow. A call that cannot succeed is settled <c>failed</c> and
/// its instance fails, with the audit records <c>operation.called</c> and <c>process.failed</c>. A call that is retried is set back to <c>pending</c>
/// with its attempt row. Every write runs in the caller's transaction.
/// </summary>
internal static class PackageCallRecords
{
    /// <summary>
    /// Finds the step, the subject entity and the operation of the call in the release's <paramref name="model"/> and the
    /// <paramref name="manifest"/> of its package. A process without a subject has no entity.
    /// </summary>
    /// <exception cref="InvalidOperationException">The release or the package lacks one of them.</exception>
    public static PackageCallTarget FindTarget(ApplicationModel model, LoadedInstance instance, LoadedPackageCall call, PackageManifest manifest)
    {
        var process = model.Processes.FirstOrDefault(candidate => candidate.Id == instance.ProcessId)
            ?? throw new InvalidOperationException($"The release has no process '{instance.ProcessId}'.");
        var entity = instance.SubjectEntityId is { } entityId
            ? model.FindEntity(entityId) ?? throw new InvalidOperationException($"The release has no entity '{entityId}'.")
            : null;
        if (!process.TryGetStep(call.Step, out var step) || step is not PackageOperationStepModel packageStep)
        {
            throw new InvalidOperationException($"The process '{process.Name}' has no package operation step '{call.Step}'.");
        }

        var operation = manifest.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, call.Operation, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The package '{call.Package}' {call.Version} has no operation '{call.Operation}'.");
        return new PackageCallTarget(model, entity, packageStep, operation);
    }

    /// <summary>
    /// Decodes the <c>output</c> of a runner's result into the .NET values of the operation's outputs. An output the result lacks is
    /// removed, which <see cref="Fit"/> would otherwise write as an explicit <c>null</c>. Returns <see langword="null"/> when a value does
    /// not decode.
    /// </summary>
    public static Dictionary<string, object?>? DecodeRunnerOutput(PackageOperation operation, JsonObject output)
    {
        try
        {
            var decoded = RunnerValues.Decode(operation.Output, output);
            foreach (var field in operation.Output.Where(field => !output.ContainsKey(field.Name)))
            {
                decoded.Remove(field.Name);
            }

            return decoded;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the outputs from the operation's result. The result fits when it holds a value of the
    /// exact .NET type for each output it holds, and a value for each required output. Returns the
    /// subject fields to write with their values, or <see langword="null"/> when it does not fit.
    /// An optional output the result leaves out writes nothing, and an explicit <c>null</c> writes
    /// <c>null</c>.
    /// </summary>
    public static List<KeyValuePair<FieldModel, object?>>? Fit(
        IReadOnlyDictionary<string, object?>? result, PackageOperation operation, PackageOperationStepModel step, EntityModel? entity)
    {
        if (result is null)
        {
            return null;
        }

        var values = new List<KeyValuePair<FieldModel, object?>>();
        foreach (var output in operation.Output)
        {
            var present = result.TryGetValue(output.Name, out var value);
            if (value is null)
            {
                if (output.Required)
                {
                    return null;
                }
            }
            else if (!HasType(value, output.Type))
            {
                return null;
            }

            var mapping = step.Output.FirstOrDefault(candidate => string.Equals(candidate.Output, output.Name, StringComparison.OrdinalIgnoreCase));
            if (present && mapping is not null)
            {
                if (entity is null || !entity.TryGetField(mapping.Field, out var field))
                {
                    return null;
                }

                values.Add(KeyValuePair.Create(field, value is DateTimeOffset time ? time.ToUniversalTime() : value));
            }
        }

        return values;
    }

    private static bool HasType(object value, FieldType type) =>
        type switch
        {
            FieldType.Text => value is string,
            FieldType.Integer => value is long,
            FieldType.Decimal => value is decimal,
            FieldType.Boolean => value is bool,
            FieldType.Date => value is DateOnly,
            FieldType.DateTime => value is DateTimeOffset,
            _ => false,
        };

    /// <summary>The full type name of <paramref name="exception"/>, the only part of it that is recorded. Its message can hold record values.</summary>
    public static string ErrorOf(Exception exception) => exception.GetType().FullName ?? exception.GetType().Name;

    /// <summary>The error of an output that does not fit the manifest.</summary>
    public static string Misfit(PackageManifest manifest, PackageOperation operation) =>
        $"The output of the operation '{operation.Name}' of the package '{manifest.Id}' {manifest.Version} did not fit its manifest.";

    /// <summary>
    /// The row of the attempt the call makes. Its number is the next attempt number of the call, which
    /// carries on after an operator retry. A package call has no HTTP status.
    /// </summary>
    public static CallAttempt AttemptRow(PackageCallAttempt attempt, string outcome, string? error, StepProblem? problem = null) =>
        new(CallAttempts.Attempt, null, attempt.StartedAt, outcome, null, error, Problem: problem);

    /// <summary>The names that label the call metric of the attempt.</summary>
    public static CallTags Tags(string tenantId, PackageCallAttempt attempt) =>
        new(tenantId, attempt.Application, AxisTelemetry.PackageTag, attempt.Call.Package, attempt.Call.Operation);

    /// <summary>Writes <paramref name="row"/>, or nothing when it is <see langword="null"/>, which means that no attempt was made.</summary>
    public static Task InsertAttemptAsync(NpgsqlTransaction transaction, CallTags tags, LoadedPackageCall call, CallAttempt? row, CancellationToken cancellationToken) =>
        row is null ? Task.CompletedTask : CallAttempts.InsertAsync(transaction, CallAttempts.Package, call.Id, row, tags, cancellationToken);

    /// <summary>The audit record <c>operation.called</c> of a call that settled in <paramref name="outcome"/>.</summary>
    public static AuditEntry Called(LoadedInstance instance, PackageCallAttempt attempt, string outcome) =>
        new(
            AuditActors.System,
            AuditActions.OperationCalled,
            instance.ApplicationId,
            instance.SubjectEntityId,
            instance.SubjectId,
            instance.Id,
            new JsonObject
            {
                ["package"] = attempt.Call.Package,
                ["version"] = attempt.Call.Version,
                ["operation"] = attempt.Call.Operation,
                ["operationId"] = attempt.Call.Id,
                ["attempts"] = attempt.Attempts,
                ["outcome"] = outcome,
            });

    /// <summary>
    /// Writes the success in the transaction: the output fields with the record update rules, the call <c>succeeded</c>, the instance
    /// <c>running</c> on <c>next</c>, with the values <paramref name="returned"/> that it keeps, the history row, the audit records,
    /// the data change triggers from the active release that <paramref name="findActive"/> finds, the attempt row and the next step's work item. Returns the failure when the record write is
    /// refused, or <see langword="null"/>. The caller rolls back on an error.
    /// </summary>
    /// <exception cref="InvalidOperationException">The instance is no longer waiting on the call's step.</exception>
    public static async Task<StepFailure?> WriteSuccessAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        LoadedInstance instance,
        PackageCallAttempt attempt,
        ApplicationModel model,
        EntityModel? entity,
        PackageOperationStepModel step,
        List<KeyValuePair<FieldModel, object?>> values,
        JsonObject returned,
        RecordCipher? cipher,
        Func<Guid, CancellationToken, Task<(Guid ReleaseId, ApplicationModel Model)?>> findActive,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        var connection = transaction.Connection!;
        Record? record = null;

        // A process without a subject has no record to lock, read or write, and no outputs to write.
        if (instance.SubjectId is { } subjectId && entity is not null)
        {
            if (values.Count > 0)
            {
                // The write can start instances through data change triggers. The start lock comes before any
                // entity table is read, so a migrating activation cannot deadlock with this transaction.
                await StartLock.TakeSharedAsync(transaction, instance.ApplicationId, cancellationToken);

                // The lock makes the read below see the latest version, and a concurrent write of the
                // record waits until this transaction ends.
                await RecordCommands.LockAsync(connection, entity, subjectId, cancellationToken);
            }

            record = await RecordQueries.GetAsync(connection, model, entity, subjectId, cipher, cancellationToken);
            if (record is null && values.Count > 0)
            {
                return new StepFailure($"No record of '{entity.Name}' has the id '{subjectId}'.", null);
            }
        }
        else if (values.Count > 0)
        {
            return new StepFailure("The step has outputs, but the process has no subject to write them to.", null);
        }

        if (!await ProcessSteps.TryResumeCallAsync(transaction, instance.Id, call.Step, attempt.Revision, step.Next, cancellationToken))
        {
            throw new InvalidOperationException($"The process instance '{instance.Id}' is no longer waiting on the step '{call.Step}'.");
        }

        var input = new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = record?.Version };
        var output = new JsonObject { ["operationId"] = call.Id, ["outcome"] = PackageCalls.Succeeded, ["next"] = step.Next };
        AuditEntry? recordAudit = null;
        Record? updated = null;
        JsonArray? changes = null;
        IReadOnlyList<EffectRecord> effects = [];
        var write = new RecordWriteContext(instance.AccountableUser, instance.DecisionTask);
        if (values.Count > 0)
        {
            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            var result = await RecordCommands.SetAsync(connection, model, entity!, call.ReleaseId, write, record!, values, now, cipher: cipher, cancellationToken: cancellationToken);
            if (result is not { Outcome: RecordWriteOutcome.Written, Record: { } written })
            {
                return new StepFailure($"The record update was rejected: {ProcessStepHandler.Describe(result)}", StepProblem.WriteRefused);
            }

            await ComputedBackfill.MarkStaleWriteAsync(connection, transaction, tenantId, model, entity!, call.ReleaseId, cancellationToken);
            updated = written;
            changes = RunValues.Changes(model, entity!, record!, written);
            effects = result.Effects;
            output["subjectVersion"] = written.Version;
            recordAudit = new AuditEntry(
                AuditActors.System,
                AuditActions.RecordUpdated,
                instance.ApplicationId,
                instance.SubjectEntityId,
                instance.SubjectId,
                instance.Id,
                new JsonObject
                {
                    ["version"] = written.Version,
                    ["fields"] = new JsonArray([.. values.Select(value => JsonValue.Create(value.Key.Name))]),
                });
        }

        await PackageCalls.SettleAsync(transaction, call.Id, PackageCalls.Succeeded, null, returned.ToJsonString(), cancellationToken);
        await CallAttempts.InsertAsync(transaction, CallAttempts.Package, call.Id, AttemptRow(attempt, CallAttempts.Succeeded, null), Tags(tenantId, attempt), cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(instance.Id, call.Step, attempt.Revision, input, output, null, null, attempt.StartedAt, Changes: changes),
            cancellationToken);
        if (recordAudit is not null)
        {
            await AuditRecords.AppendAsync(transaction, recordAudit, cancellationToken);
        }

        if (updated is not null)
        {
            var active = await findActive(instance.ApplicationId, cancellationToken);
            if (active is { } activeNow)
            {
                await DataChangeTriggerStarts.StartAfterUpdateAsync(
                    transaction,
                    tenantId,
                    activeNow.Model,
                    activeNow.ReleaseId,
                    call.ReleaseId,
                    entity!,
                    record!,
                    updated,
                    [],
                    write,
                    logger,
                    cancellationToken);
            }

            await EffectRecordWrites.AppendAsync(
                transaction,
                tenantId,
                active?.Model,
                active?.ReleaseId ?? call.ReleaseId,
                call.ReleaseId,
                instance.ApplicationId,
                instance.Id,
                AuditActors.System,
                effects,
                write,
                logger,
                cancellationToken);
        }

        await AuditRecords.AppendAsync(transaction, Called(instance, attempt, PackageCalls.Succeeded), cancellationToken);
        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            tenantId,
            ProcessStarts.StepWorkItemKind,
            DateTimeOffset.UtcNow,
            cancellationToken,
            processInstanceId: instance.Id);
        return null;
    }

    /// <summary>
    /// The wait before the next attempt, which is the step's backoff doubled for each attempt made, or <see langword="null"/> when the retry
    /// policy has no attempt left.
    /// </summary>
    public static TimeSpan? RetryDelay(PackageCallAttempt attempt) =>
        attempt.Attempts < attempt.Call.Retry.Attempts ? ConnectorCallHandler.Backoff(attempt.Call.Retry, attempt.Attempts) : null;

    /// <summary>
    /// Sets the call back to <c>pending</c> with <paramref name="error"/>, keeping its stored input so the next attempt runs under the same
    /// operation identity, and writes <paramref name="row"/> unless it is <see langword="null"/>, which means that no attempt was made. It writes
    /// no history row and no audit record. The caller makes the next attempt due.
    /// </summary>
    public static async Task WriteRetryAsync(
        NpgsqlTransaction transaction, string tenantId, PackageCallAttempt attempt, string error, CallAttempt? row, CancellationToken cancellationToken)
    {
        await PackageCalls.RetryAsync(transaction, attempt.Call.Id, error, cancellationToken);
        await InsertAttemptAsync(transaction, Tags(tenantId, attempt), attempt.Call, row, cancellationToken);
    }

    /// <summary>
    /// Settles the call <c>failed</c> with <paramref name="error"/> and fails its instance, with a
    /// history row and the audit records <c>operation.called</c> and <c>process.failed</c>. It writes
    /// <paramref name="row"/> unless it is <see langword="null"/>, which means that no attempt was made. Returns
    /// <see langword="false"/> and writes nothing when the instance changed since the attempt.
    /// </summary>
    public static async Task<bool> WriteFailureAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        LoadedInstance instance,
        PackageCallAttempt attempt,
        string error,
        CallAttempt? row,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        if (!await ProcessSteps.TryFailWaitingAsync(transaction, instance.Id, call.Step, attempt.Revision, cancellationToken))
        {
            return false;
        }

        await PackageCalls.SettleAsync(transaction, call.Id, PackageCalls.Failed, error, null, cancellationToken);
        await InsertAttemptAsync(transaction, Tags(tenantId, attempt), call, row, cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(
                instance.Id,
                call.Step,
                attempt.Revision,
                new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = null },
                new JsonObject { ["operationId"] = call.Id, ["outcome"] = PackageCalls.Failed },
                null,
                error,
                attempt.StartedAt,
                row?.Problem),
            cancellationToken);
        await AuditRecords.AppendAsync(transaction, Called(instance, attempt, PackageCalls.Failed), cancellationToken);
        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                AuditActors.System,
                AuditActions.ProcessFailed,
                instance.ApplicationId,
                instance.SubjectEntityId,
                instance.SubjectId,
                instance.Id,
                new JsonObject { ["step"] = call.Step }),
            cancellationToken);
        return true;
    }
}

/// <summary>
/// The attempt a call makes: the call, the instance revision it waits at, the attempts made with this one, and the time that starts the
/// step's history row. <paramref name="Application"/> is the application's name, which labels the call metric.
/// </summary>
internal sealed record PackageCallAttempt(LoadedPackageCall Call, long Revision, int Attempts, DateTimeOffset StartedAt, string? Application = null);

/// <summary>What the call's release declares for it: the model, the subject entity if the process has one, the step and the operation.</summary>
internal sealed record PackageCallTarget(ApplicationModel Model, EntityModel? Entity, PackageOperationStepModel Step, PackageOperation Operation);
