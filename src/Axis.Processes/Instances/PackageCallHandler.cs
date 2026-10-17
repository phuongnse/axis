using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Extensions;
using Axis.Processes.Work;
using Axis.Tenancy;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Runs the package call a <c>package.call</c> work item names, with no transaction open while the
/// operation runs. A short transaction first marks the call <c>sending</c>, counts the attempt and
/// extends the item's lease by the operation's timeout. The operation then runs from the package
/// version the release pins, with the stored input and the operation identity in its context, under
/// a token that is cancelled at the timeout. An outcome transaction records the result. An output
/// that fits the manifest writes the output fields, starts the data change triggers of the update
/// from the active release, settles the call <c>succeeded</c> and resumes
/// the instance on <c>next</c>. A <see cref="RetryableException"/>, and a pinned version that the
/// worker does not hold, set the call back to <c>pending</c> while the step's retry policy has
/// attempts left. The item is then due again after the backoff, doubled for each attempt and at most
/// an hour, and the next attempt runs under the same operation identity. A retry writes no history row
/// and no audit record. A timeout, and a call found already <c>sending</c>, have an unknown outcome:
/// the operation may have acted, so it is retried only when it is <c>idempotent</c> and attempts are
/// left. Otherwise the call is left <c>unknown</c> and the instance stays waiting. An output that does
/// not fit, any other exception, a release or manifest that does not match, and a retryable error
/// when no attempts are left settle the call <c>failed</c> and fail the instance.
/// Before the operation runs, the worker resolves the <c>url</c> and <c>secret</c> bindings its
/// manifest declares for the tenant. A binding the tenant lacks, or an <c>http</c> URL outside
/// Development, is a retryable error and the operation does not run. The context gives the
/// operation only those bindings. An operation that asks for another binding, or sends a request
/// to a host no declared <c>url</c> binding names, fails the call at once, even if it catches the
/// exception. The error names the operation, the package and the binding, never a value.
/// The retry policy and the <c>idempotent</c> flag are stored with the call, so a worker that cannot load the pinned
/// version can still decide. Each transaction checks the claim token, and the outcome needs the
/// instance still waiting on the step at the revision the attempt saw. Each outcome transaction
/// also writes one row to <c>axis.process_call_attempts</c>: the attempt with its times, its outcome
/// class and a fixed error text, and no HTTP status. A call found <c>sending</c> gets an attempt row
/// with the outcome <c>unknown</c>. No input value and no exception message is stored or logged.
/// </summary>
internal sealed partial class PackageCallHandler(
    ReleaseModelCache models,
    PackageCatalog packages,
    IDeploymentBindings bindings,
    ConnectorCallSettings settings,
    PackageHttpTransport transport,
    ILogger<PackageCallHandler> logger,
    Func<InstalledPackage, string, IOperation>? createOperation = null) : IExternalWorkItemHandler
{
    /// <summary>The error of a call that a worker finds still <c>sending</c> from an earlier attempt.</summary>
    internal const string UnknownOutcome = "The earlier attempt of the call has an unknown outcome.";

    /// <summary>The error of a call whose operation did not finish within its timeout.</summary>
    internal const string TimedOut = "The operation did not finish within its timeout.";

    public string Kind => ProcessStarts.PackageCallWorkItemKind;

    public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(connection);

        Attempt attempt;
        Target? target = null;
        string? error = null;
        var errorClass = ErrorClass.Failed;
        var counted = true;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var call = item.PackageCallId is { } callId ? await PackageCalls.LoadAsync(transaction, callId, cancellationToken) : null;
            var instance = call is null ? null : await ProcessSteps.LoadAsync(transaction, call.ProcessInstanceId, cancellationToken);
            if (call is not { State: PackageCalls.Pending or PackageCalls.Sending }
                || instance is not { State: ProcessStarts.Waiting }
                || instance.Step != call.Step)
            {
                // The call settled or is unknown, or its instance moved on, so there is nothing to run.
                LogDiscarded(item.Id, item.PackageCallId, call?.State, instance?.State);
                if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    LogLostClaim(item.Id, item.PackageCallId);
                    return WorkItemOutcome.LostClaim;
                }

                await transaction.CommitAsync(cancellationToken);
                return WorkItemOutcome.Completed;
            }

            attempt = new Attempt(call, instance.Revision, call.Attempts, instance.Now);
            var package = call.State == PackageCalls.Sending ? null : packages.Find(call.Package, call.Version);
            if (call.State == PackageCalls.Sending)
            {
                // A worker crashed after it marked the call, so the operation may have acted. The
                // attempt the crashed worker made is already counted.
                errorClass = ErrorClass.Unknown;
                error = UnknownOutcome;
                await transaction.RollbackAsync(cancellationToken);
            }
            else if (package is null)
            {
                // The release cannot be rebuilt without the version, and the operation never ran, so
                // the attempt is counted without marking the call.
                var attempts = await PackageCalls.CountAttemptAsync(transaction, call.Id, cancellationToken)
                    ?? throw new InvalidOperationException($"The package call '{call.Id}' is no longer pending.");
                attempt = attempt with { Attempts = attempts };
                errorClass = ErrorClass.Retryable;
                error = $"The package '{call.Package}' {call.Version} is not installed on this worker.";
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                try
                {
                    target = await FindTargetAsync(new WorkItemContext(item, connection, transaction), call, instance, package, cancellationToken);
                }
                catch (InvalidOperationException exception)
                {
                    // The operation never ran, and the attempt is not counted.
                    error = exception.Message;
                    counted = false;
                }

                if (target is not null)
                {
                    var attempts = await PackageCalls.MarkSendingAsync(transaction, call.Id, cancellationToken)
                        ?? throw new InvalidOperationException($"The package call '{call.Id}' is no longer pending.");
                    attempt = attempt with { Attempts = attempts };

                    // No other worker claims the item while the operation runs.
                    if (!await WorkItemQueue.ExtendLeaseAsync(connection, transaction, item, target.Step.Timeout + item.Lease, cancellationToken))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        LogLostClaim(item.Id, item.PackageCallId);
                        return WorkItemOutcome.LostClaim;
                    }

                    await transaction.CommitAsync(cancellationToken);
                }
                else
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
            }
        }

        if (target is null)
        {
            // A release or a manifest that does not match cannot be fixed by a retry. A call that is
            // found sending or lacks its version is settled by the retry policy.
            return await SettleAttemptAsync(item, connection, attempt, errorClass, error!, counted, cancellationToken);
        }

        if (ReadInput(attempt.Call.Input, target.Operation) is not { } input)
        {
            var misfit = $"The stored input of the call did not fit the operation '{target.Operation.Name}' of the package '{target.Package.Manifest.Id}'.";
            return await FailAsync(item, connection, attempt, misfit, AttemptRow(attempt, CallAttempts.Failed, misfit), cancellationToken);
        }

        // The bindings are resolved before the operation runs, so a missing one leaves nothing run.
        if (!TryCreateContext(item, target, attempt.Call, out var context, out var bindingError))
        {
            return await SettleAttemptAsync(item, connection, attempt, ErrorClass.Retryable, bindingError, true, cancellationToken);
        }

        var run = await RunAsync(target, attempt.Call, input, context, cancellationToken);
        if (context.Violation is { } violation)
        {
            // The operation asked for more than it declares. A retry cannot fix that, and the
            // operation may have swallowed the exception, so this holds whatever it returned.
            return await FailAsync(item, connection, attempt, violation, AttemptRow(attempt, CallAttempts.Failed, violation), cancellationToken);
        }

        if (run.Timeout)
        {
            // The operation may have acted.
            return await SettleAttemptAsync(item, connection, attempt, ErrorClass.Unknown, TimedOut, true, cancellationToken);
        }

        if (run.Error is { } runError)
        {
            return await SettleAttemptAsync(item, connection, attempt, run.Retryable ? ErrorClass.Retryable : ErrorClass.Failed, runError, true, cancellationToken);
        }

        if (Fit(run.Output, target) is not { } values)
        {
            var misfit = $"The output of the operation '{target.Operation.Name}' of the package '{target.Package.Manifest.Id}' {target.Package.Manifest.Version} did not fit its manifest.";
            return await FailAsync(item, connection, attempt, misfit, AttemptRow(attempt, CallAttempts.Failed, misfit), cancellationToken);
        }

        return await SucceedAsync(item, connection, attempt, target, values, cancellationToken);
    }

    /// <summary>Finds the step, the subject entity and the operation of the call in the release and the installed <paramref name="package"/>.</summary>
    /// <exception cref="InvalidOperationException">The release or the package lacks one of them.</exception>
    private async Task<Target> FindTargetAsync(
        WorkItemContext context,
        LoadedPackageCall call,
        LoadedInstance instance,
        InstalledPackage package,
        CancellationToken cancellationToken)
    {
        var model = await models.GetAsync(context, call.ReleaseId, cancellationToken);
        var process = model.Processes.FirstOrDefault(candidate => candidate.Id == instance.ProcessId)
            ?? throw new InvalidOperationException($"The release has no process '{instance.ProcessId}'.");
        var entity = model.FindEntity(instance.SubjectEntityId)
            ?? throw new InvalidOperationException($"The release has no entity '{instance.SubjectEntityId}'.");
        if (!process.TryGetStep(call.Step, out var step) || step is not PackageOperationStepModel packageStep)
        {
            throw new InvalidOperationException($"The process '{process.Name}' has no package operation step '{call.Step}'.");
        }

        var operation = package.Manifest.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, call.Operation, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The package '{call.Package}' {call.Version} has no operation '{call.Operation}'.");
        return new Target(model, entity, packageStep, package, operation);
    }

    /// <summary>
    /// Reads the stored input into the .NET values the operation gets, one for each input of its
    /// manifest, in the form the expression interpreter gives. An input the call stores as null, or
    /// not at all, is <see langword="null"/>. Returns <see langword="null"/> when the input is not
    /// stored or a value has the wrong type.
    /// </summary>
    private static Dictionary<string, object?>? ReadInput(string? stored, PackageOperation operation)
    {
        if (stored is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(stored);
        var values = new Dictionary<string, object?>();
        foreach (var field in operation.Input)
        {
            object? value = null;
            if (document.RootElement.TryGetProperty(field.Name, out var element)
                && element.ValueKind != JsonValueKind.Null
                && !ConnectorCallHandler.TryRead(element, field.Type, out value))
            {
                return null;
            }

            values[field.Name] = value;
        }

        return values;
    }

    /// <summary>
    /// Resolves the <c>url</c> and <c>secret</c> bindings the operation declares for the current
    /// tenant into its context. Returns <see langword="false"/> with the error when a binding is
    /// missing or of the wrong kind, or when a URL is not <c>https</c> and the host does not allow
    /// <c>http</c>. The error never holds a binding's value.
    /// </summary>
    private bool TryCreateContext(
        ClaimedWorkItem item,
        Target target,
        LoadedPackageCall call,
        [NotNullWhen(true)] out PackageOperationContext? context,
        [NotNullWhen(false)] out string? error)
    {
        var capabilities = target.Operation.Capabilities;
        var urls = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        var secrets = new Dictionary<string, SecretValue>(StringComparer.OrdinalIgnoreCase);
        context = null;
        try
        {
            foreach (var name in capabilities.Urls)
            {
                var value = bindings.Resolve(name).Value;
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !settings.AllowHttp))
                {
                    error = $"Tenant '{item.TenantId}' binding '{name}' is not an https URL. Only Development allows http.";
                    return false;
                }

                urls[name] = uri;
            }

            foreach (var name in capabilities.Secrets)
            {
                secrets[name] = bindings.ResolveSecret(name);
            }
        }
        catch (InvalidOperationException exception)
        {
            error = exception.Message;
            return false;
        }

        error = null;
        context = new PackageOperationContext(call.Id, target.Package.Manifest.Id, target.Operation.Name, urls, secrets, transport.Handler);
        return true;
    }

    /// <summary>
    /// Creates the operation and runs it, with no transaction open, under a token that is cancelled
    /// at the step's timeout. The result holds the output, or the error of an exception as its type
    /// name and whether it is retryable, or says that the run timed out. Worker shutdown is not caught.
    /// </summary>
    private async Task<Run> RunAsync(
        Target target,
        LoadedPackageCall call,
        Dictionary<string, object?> input,
        PackageOperationContext context,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(target.Step.Timeout);
        try
        {
            var operation = createOperation is null ? target.Package.CreateOperation(call.Operation) : createOperation(target.Package, call.Operation);

            // An operation that ignores its token keeps running, but the worker stops waiting for it.
            var output = await operation.RunAsync(input, context, timeout.Token).WaitAsync(timeout.Token);
            return new Run(output, null, false, false);
        }
        catch (Exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new Run(null, null, false, true);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The message can hold input values or secrets, so only the type is kept.
            return new Run(null, ErrorOf(exception), exception is RetryableException, false);
        }
    }

    /// <summary>The full type name of <paramref name="exception"/>, the only part of it that is recorded.</summary>
    private static string ErrorOf(Exception exception) => exception.GetType().FullName ?? exception.GetType().Name;

    /// <summary>
    /// Reads the outputs from the operation's result. The result fits when it holds a value of the
    /// exact .NET type for each output it holds, and a value for each required output. Returns the
    /// subject fields to write with their values, or <see langword="null"/> when it does not fit.
    /// An optional output the result leaves out writes nothing, and an explicit <c>null</c> writes
    /// <c>null</c>.
    /// </summary>
    private static List<KeyValuePair<FieldModel, object?>>? Fit(IReadOnlyDictionary<string, object?>? result, Target target)
    {
        if (result is null)
        {
            return null;
        }

        var values = new List<KeyValuePair<FieldModel, object?>>();
        foreach (var output in target.Operation.Output)
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

            var mapping = target.Step.Output.FirstOrDefault(candidate => string.Equals(candidate.Output, output.Name, StringComparison.OrdinalIgnoreCase));
            if (present && mapping is not null)
            {
                if (!target.Entity.TryGetField(mapping.Field, out var field))
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

    /// <summary>
    /// Records a fitting output in one transaction: the output fields with the record update rules,
    /// the call <c>succeeded</c>, the instance <c>running</c> on <c>next</c>, the history row, the
    /// audit records, the next step's work item and the attempt row. A refused write or any other
    /// error rolls it back and fails the call instead, and the attempt row then says it failed.
    /// </summary>
    private async Task<WorkItemOutcome> SucceedAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        Target target,
        List<KeyValuePair<FieldModel, object?>> values,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        string? failure;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id, call.Id);
                return WorkItemOutcome.LostClaim;
            }

            var instance = await ProcessSteps.LoadAsync(transaction, call.ProcessInstanceId, cancellationToken);
            if (!IsWaitingAt(instance, call, attempt))
            {
                LogStale(call.Id, call.ProcessInstanceId, call.Step, attempt.Revision);
                await transaction.CommitAsync(cancellationToken);
                return WorkItemOutcome.Completed;
            }

            try
            {
                failure = await WriteSuccessAsync(item, transaction, instance!, attempt, target, values, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The message can hold record values, so only the type is kept.
                failure = ErrorOf(exception);
            }

            if (failure is null)
            {
                await transaction.CommitAsync(cancellationToken);
                LogSucceeded(call.Id, call.ProcessInstanceId);
                return WorkItemOutcome.Completed;
            }

            await transaction.RollbackAsync(cancellationToken);
        }

        return await FailAsync(item, connection, attempt, failure, AttemptRow(attempt, CallAttempts.Failed, failure), cancellationToken);
    }

    /// <summary>Writes the success in the transaction. Returns the error when the record write is refused, or <see langword="null"/>.</summary>
    private async Task<string?> WriteSuccessAsync(
        ClaimedWorkItem item,
        NpgsqlTransaction transaction,
        LoadedInstance instance,
        Attempt attempt,
        Target target,
        List<KeyValuePair<FieldModel, object?>> values,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        var connection = transaction.Connection!;
        var entity = target.Entity;
        if (values.Count > 0)
        {
            // The write can start instances through data change triggers. The start lock comes before any
            // entity table is read, so a migrating activation cannot deadlock with this transaction.
            await StartLock.TakeSharedAsync(transaction, instance.ApplicationId, cancellationToken);

            // The lock makes the read below see the latest version, and a concurrent write of the
            // record waits until this transaction ends.
            await RecordCommands.LockAsync(connection, entity, instance.SubjectId, cancellationToken);
        }

        var record = await RecordQueries.GetAsync(connection, target.Model, entity, instance.SubjectId, cancellationToken);
        if (record is null && values.Count > 0)
        {
            return $"No record of '{entity.Name}' has the id '{instance.SubjectId}'.";
        }

        if (!await ProcessSteps.TryResumeCallAsync(transaction, instance.Id, call.Step, attempt.Revision, target.Step.Next, cancellationToken))
        {
            throw new InvalidOperationException($"The process instance '{instance.Id}' is no longer waiting on the step '{call.Step}'.");
        }

        var input = new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = record?.Version };
        var output = new JsonObject { ["operationId"] = call.Id, ["outcome"] = PackageCalls.Succeeded, ["next"] = target.Step.Next };
        AuditEntry? recordAudit = null;
        Record? updated = null;
        if (values.Count > 0)
        {
            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            var result = await RecordCommands.SetAsync(connection, target.Model, entity, call.ReleaseId, record!, values, now, cancellationToken);
            if (result is not { Outcome: RecordWriteOutcome.Written, Record: { } written })
            {
                return $"The record update was rejected: {ProcessStepHandler.Describe(result)}";
            }

            updated = written;
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

        await PackageCalls.SettleAsync(transaction, call.Id, PackageCalls.Succeeded, null, cancellationToken);
        await CallAttempts.InsertAsync(transaction, CallAttempts.Package, call.Id, AttemptRow(attempt, CallAttempts.Succeeded, null), cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(instance.Id, call.Step, attempt.Revision, input, output, null, null, attempt.StartedAt),
            cancellationToken);
        if (recordAudit is not null)
        {
            await AuditRecords.AppendAsync(transaction, recordAudit, cancellationToken);
        }

        if (updated is not null
            && await models.FindActiveAsync(new WorkItemContext(item, connection, transaction), instance.ApplicationId, cancellationToken) is { } active)
        {
            await DataChangeTriggerStarts.StartAfterUpdateAsync(
                transaction,
                item.TenantId,
                active.Model,
                active.ReleaseId,
                call.ReleaseId,
                entity,
                record!,
                updated,
                [],
                logger,
                cancellationToken);
        }

        await AuditRecords.AppendAsync(transaction, Called(instance, attempt, PackageCalls.Succeeded), cancellationToken);
        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            item.TenantId,
            ProcessStarts.StepWorkItemKind,
            DateTimeOffset.UtcNow,
            cancellationToken,
            processInstanceId: instance.Id);
        return null;
    }

    /// <summary>
    /// Records an attempt that ended with <paramref name="error"/> of <paramref name="errorClass"/>.
    /// A retryable error is retried while attempts are left, and an unknown outcome only when the
    /// operation is also <c>idempotent</c>. An unknown outcome that is not retried leaves the call
    /// <c>unknown</c>. Anything else fails the call. The attempt row records the class of the attempt,
    /// whatever happens to the call. It is left out when <paramref name="counted"/> is
    /// <see langword="false"/>, because no attempt was made.
    /// </summary>
    private Task<WorkItemOutcome> SettleAttemptAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        ErrorClass errorClass,
        string error,
        bool counted,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        var attemptsLeft = attempt.Attempts < call.Retry.Attempts;
        var row = counted
            ? AttemptRow(
                attempt,
                errorClass switch
                {
                    ErrorClass.Retryable => CallAttempts.Retryable,
                    ErrorClass.Unknown => CallAttempts.Unknown,
                    _ => CallAttempts.Failed,
                },
                error)
            : null;
        return errorClass switch
        {
            ErrorClass.Retryable when attemptsLeft =>
                RetryAsync(item, connection, attempt, error, ConnectorCallHandler.Backoff(call.Retry, attempt.Attempts), row, cancellationToken),
            ErrorClass.Unknown when attemptsLeft && call.Idempotent =>
                RetryAsync(item, connection, attempt, error, ConnectorCallHandler.Backoff(call.Retry, attempt.Attempts), row, cancellationToken),
            ErrorClass.Unknown => LeaveUnknownAsync(item, connection, attempt, error, row, cancellationToken),
            _ => FailAsync(item, connection, attempt, error, row, cancellationToken),
        };
    }

    /// <summary>
    /// Sets the call back to <c>pending</c> with the attempt's error, and keeps the item, due again
    /// after <paramref name="delay"/>. The next attempt runs the stored input under the same
    /// operation identity. It writes <paramref name="row"/>, and no history row and no audit record.
    /// </summary>
    private async Task<WorkItemOutcome> RetryAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        string error,
        TimeSpan delay,
        CallAttempt? row,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var instance = await ProcessSteps.LoadAsync(transaction, call.ProcessInstanceId, cancellationToken);
        if (!IsWaitingAt(instance, call, attempt))
        {
            if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id, call.Id);
                return WorkItemOutcome.LostClaim;
            }

            LogStale(call.Id, call.ProcessInstanceId, call.Step, attempt.Revision);
            await transaction.CommitAsync(cancellationToken);
            return WorkItemOutcome.Completed;
        }

        await PackageCalls.RetryAsync(transaction, call.Id, error, cancellationToken);
        await InsertAttemptAsync(transaction, call, row, cancellationToken);
        if (!await WorkItemQueue.RescheduleAsync(connection, transaction, item, delay, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, call.Id);
            return WorkItemOutcome.LostClaim;
        }

        await transaction.CommitAsync(cancellationToken);
        LogRetrying(call.Id, call.ProcessInstanceId, attempt.Attempts, error, delay);
        return WorkItemOutcome.Rescheduled;
    }

    /// <summary>
    /// Leaves the call <c>unknown</c> with the attempt's error, and keeps its stored input. The
    /// item is deleted and the instance stays waiting. A history row with the outcome
    /// <c>unknown</c> makes the call visible. It writes no audit record, because the call has not
    /// settled. It writes <paramref name="row"/>.
    /// </summary>
    private async Task<WorkItemOutcome> LeaveUnknownAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        string error,
        CallAttempt? row,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var instance = await ProcessSteps.LoadAsync(transaction, call.ProcessInstanceId, cancellationToken);
        var waiting = IsWaitingAt(instance, call, attempt);
        if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, call.Id);
            return WorkItemOutcome.LostClaim;
        }

        if (!waiting)
        {
            LogStale(call.Id, call.ProcessInstanceId, call.Step, attempt.Revision);
            await transaction.CommitAsync(cancellationToken);
            return WorkItemOutcome.Completed;
        }

        await PackageCalls.MarkUnknownAsync(transaction, call.Id, error, cancellationToken);
        await InsertAttemptAsync(transaction, call, row, cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(
                instance!.Id,
                call.Step,
                attempt.Revision,
                new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = null },
                new JsonObject { ["operationId"] = call.Id, ["outcome"] = PackageCalls.Unknown },
                null,
                error,
                attempt.StartedAt),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogUnknown(call.Id, instance.Id, attempt.Attempts, error);
        return WorkItemOutcome.Completed;
    }

    /// <summary>
    /// Settles the call <c>failed</c> with <paramref name="error"/> and fails its instance, with a
    /// history row and the audit records <c>operation.called</c> and <c>process.failed</c>. It writes
    /// <paramref name="row"/> unless it is <see langword="null"/>, which means that no attempt was made.
    /// </summary>
    private async Task<WorkItemOutcome> FailAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        string error,
        CallAttempt? row,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, call.Id);
            return WorkItemOutcome.LostClaim;
        }

        var instance = await ProcessSteps.LoadAsync(transaction, call.ProcessInstanceId, cancellationToken);
        if (!IsWaitingAt(instance, call, attempt)
            || !await ProcessSteps.TryFailWaitingAsync(transaction, instance!.Id, call.Step, attempt.Revision, cancellationToken))
        {
            LogStale(call.Id, call.ProcessInstanceId, call.Step, attempt.Revision);
            await transaction.CommitAsync(cancellationToken);
            return WorkItemOutcome.Completed;
        }

        await PackageCalls.SettleAsync(transaction, call.Id, PackageCalls.Failed, error, cancellationToken);
        await InsertAttemptAsync(transaction, call, row, cancellationToken);
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
                attempt.StartedAt),
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
        await transaction.CommitAsync(cancellationToken);
        LogFailed(call.Id, instance.Id, error);
        return WorkItemOutcome.Failed;
    }

    /// <summary>The row of the attempt the worker makes, numbered as the call counts it. A package call has no HTTP status.</summary>
    private static CallAttempt AttemptRow(Attempt attempt, string outcome, string? error) =>
        new(CallAttempts.Attempt, attempt.Attempts, attempt.StartedAt, outcome, null, error);

    private static Task InsertAttemptAsync(NpgsqlTransaction transaction, LoadedPackageCall call, CallAttempt? row, CancellationToken cancellationToken) =>
        row is null ? Task.CompletedTask : CallAttempts.InsertAsync(transaction, CallAttempts.Package, call.Id, row, cancellationToken);

    private static bool IsWaitingAt(LoadedInstance? instance, LoadedPackageCall call, Attempt attempt) =>
        instance is { State: ProcessStarts.Waiting } && instance.Step == call.Step && instance.Revision == attempt.Revision;

    private static AuditEntry Called(LoadedInstance instance, Attempt attempt, string outcome) =>
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

    [LoggerMessage(LogLevel.Warning, "Package call work item {ItemId} for call {CallId} was discarded: the call is {CallState} and its instance is {InstanceState}.")]
    private partial void LogDiscarded(Guid itemId, Guid? callId, string? callState, string? instanceState);

    [LoggerMessage(LogLevel.Warning, "Package call work item {ItemId} for call {CallId} was claimed by another worker, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId, Guid? callId);

    [LoggerMessage(LogLevel.Warning, "Package call {CallId} of process instance {InstanceId} was made at revision {Revision} of step {Step}, but the instance changed since, so its outcome was not recorded.")]
    private partial void LogStale(Guid callId, Guid instanceId, string step, long revision);

    [LoggerMessage(LogLevel.Information, "Package call {CallId} of process instance {InstanceId} succeeded.")]
    private partial void LogSucceeded(Guid callId, Guid instanceId);

    [LoggerMessage(LogLevel.Warning, "Package call {CallId} of process instance {InstanceId} failed: {Error}")]
    private partial void LogFailed(Guid callId, Guid instanceId, string error);

    [LoggerMessage(LogLevel.Warning, "Package call {CallId} of process instance {InstanceId} will be retried after attempt {Attempts} in {Delay}: {Error}")]
    private partial void LogRetrying(Guid callId, Guid instanceId, int attempts, string error, TimeSpan delay);

    [LoggerMessage(LogLevel.Warning, "Package call {CallId} of process instance {InstanceId} has an unknown outcome after {Attempts} attempts and is not run again: {Error}")]
    private partial void LogUnknown(Guid callId, Guid instanceId, int attempts, string error);

    /// <summary>
    /// The attempt a worker makes: the call, the instance revision it waits at, the attempts made
    /// with this one, and the time of the mark transaction, which starts the step's history row.
    /// </summary>
    private sealed record Attempt(LoadedPackageCall Call, long Revision, int Attempts, DateTimeOffset StartedAt);

    /// <summary>What the call's release and the host declare for it.</summary>
    private sealed record Target(
        ApplicationModel Model,
        EntityModel Entity,
        PackageOperationStepModel Step,
        InstalledPackage Package,
        PackageOperation Operation);

    /// <summary>The result of a run: the output, the error of an exception and whether it is retryable, or that it timed out.</summary>
    private sealed record Run(IReadOnlyDictionary<string, object?>? Output, string? Error, bool Retryable, bool Timeout);

    /// <summary>How an attempt that did not succeed is handled.</summary>
    private enum ErrorClass
    {
        /// <summary>The call fails at once.</summary>
        Failed,

        /// <summary>The operation did not act, so the call is retried while attempts are left.</summary>
        Retryable,

        /// <summary>The operation may have acted. The call is retried only when it is <c>idempotent</c>.</summary>
        Unknown,
    }
}
