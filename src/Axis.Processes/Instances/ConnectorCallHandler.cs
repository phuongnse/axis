using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Core.Telemetry;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Processes.Work;
using Axis.Tenancy;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Sends the connector call a <c>connector.call</c> work item names, with no transaction open while
/// the request runs. A short transaction first marks the call <c>sending</c>, counts the attempt and
/// extends the item's lease by the connector's timeout. The request then carries the stored input,
/// the auth from the tenant's secret binding and the operation identity as its idempotency key. An
/// outcome transaction records the result. A <c>2xx</c> whose body fits the output writes the
/// output fields, starts the data change triggers of the update from the active release, settles
/// the call <c>succeeded</c> and resumes the instance on <c>next</c>. A
/// retryable error, and an unknown outcome on a connector that declares <c>idempotency</c>, set the
/// call back to <c>pending</c> and keep the item, due again after the step's backoff, while attempts
/// are left. Any other unknown outcome leaves the call <c>unknown</c> and the instance waiting. When
/// the operation declares a lookup, the item is kept and due at once, and reconciles the call: the
/// lookup asks the system with the operation identity. Found settles the call as succeeded with the
/// lookup's output. Not found sets it back to <c>pending</c> for its next attempt, or fails it when
/// no attempts are left. A failed lookup keeps it <c>unknown</c>, and the lookup is tried again after
/// a doubling backoff. Without a lookup, the item is deleted and a history row shows the call. Any
/// other error, or a retryable one with no attempts left, settles the call <c>failed</c> and fails
/// the instance. A call found already <c>sending</c> is never sent again at once: its earlier
/// attempt counts as an unknown outcome. Each transaction checks the claim token, and the outcome
/// needs the instance still waiting on the step at the revision the attempt saw. Each outcome
/// transaction also writes one row to <c>axis.process_call_attempts</c>: the attempt, or the lookup,
/// with its times, its outcome class, its status and a fixed error text. A call found <c>sending</c>
/// gets an attempt row with the outcome <c>unknown</c>. No request or response body and no secret
/// value is stored or logged.
/// </summary>
internal sealed partial class ConnectorCallHandler(
    ReleaseModelCache models,
    IDeploymentBindings bindings,
    ConnectorClient client,
    ConnectorCallSettings settings,
    ILogger<ConnectorCallHandler> logger) : IExternalWorkItemHandler
{
    /// <summary>The error of a call that a worker finds still <c>sending</c> from an earlier attempt.</summary>
    internal const string UnknownOutcome = "The earlier attempt of the call has an unknown outcome.";

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>The longest wait between two attempts.</summary>
    private static readonly TimeSpan _maxBackoff = TimeSpan.FromHours(1);

    public string Kind => ProcessStarts.ConnectorCallWorkItemKind;

    public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(connection);

        Attempt attempt;
        Target? target = null;
        ConnectorOperationModel? lookup = null;
        string? error = null;
        var sending = false;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var call = item.ConnectorCallId is { } callId ? await ConnectorCalls.LoadAsync(transaction, callId, cancellationToken) : null;
            var instance = call is null ? null : await ProcessSteps.LoadAsync(transaction, call.ProcessInstanceId, cancellationToken);
            if (call is not { State: ConnectorCalls.Pending or ConnectorCalls.Sending or ConnectorCalls.Unknown }
                || instance is not { State: ProcessStarts.Waiting }
                || instance.Step != call.Step)
            {
                // The call settled, or its instance moved on, so there is nothing to send.
                LogDiscarded(item.Id, item.ConnectorCallId, call?.State, instance?.State);
                if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    LogLostClaim(item.Id, item.ConnectorCallId);
                    return WorkItemOutcome.LostClaim;
                }

                await transaction.CommitAsync(cancellationToken);
                return WorkItemOutcome.Completed;
            }

            attempt = new Attempt(call, instance.Revision, call.Attempts, instance.Now);
            try
            {
                target = await FindTargetAsync(new WorkItemContext(item, connection, transaction), call, instance, cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                error = exception.Message;
            }

            // A worker crashed after it marked the call, so its request may have reached the system.
            sending = call.State == ConnectorCalls.Sending;
            if (target is not null && call.State == ConnectorCalls.Unknown)
            {
                lookup = FindLookup(target);
                if (lookup is null)
                {
                    // Only a call whose operation declares a lookup keeps its item, so this should not happen.
                    LogDiscarded(item.Id, item.ConnectorCallId, call.State, instance.State);
                    if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        LogLostClaim(item.Id, item.ConnectorCallId);
                        return WorkItemOutcome.LostClaim;
                    }

                    await transaction.CommitAsync(cancellationToken);
                    return WorkItemOutcome.Completed;
                }

                // A lookup is no attempt, so nothing is counted. No other worker claims the item while it runs.
                if (!await WorkItemQueue.ExtendLeaseAsync(connection, transaction, item, target.Connector.Timeout + item.Lease, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    LogLostClaim(item.Id, item.ConnectorCallId);
                    return WorkItemOutcome.LostClaim;
                }

                await transaction.CommitAsync(cancellationToken);
            }
            else if (target is not null && !sending)
            {
                var attempts = await ConnectorCalls.MarkSendingAsync(transaction, call.Id, cancellationToken)
                    ?? throw new InvalidOperationException($"The connector call '{call.Id}' is no longer pending.");
                attempt = attempt with { Attempts = attempts };

                // No other worker claims the item while the request runs.
                if (!await WorkItemQueue.ExtendLeaseAsync(connection, transaction, item, target.Connector.Timeout + item.Lease, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    LogLostClaim(item.Id, item.ConnectorCallId);
                    return WorkItemOutcome.LostClaim;
                }

                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }
        }

        if (target is null)
        {
            // Retrying cannot add what the release lacks. No attempt was counted, unless a crashed worker made one.
            return await FailAsync(
                item,
                connection,
                attempt,
                null,
                error!,
                sending ? AttemptRow(attempt, CallAttempts.Unknown, null, UnknownOutcome) : null,
                cancellationToken);
        }

        if (lookup is not null)
        {
            using var lookupSpan = StartCallSpan(AxisTelemetry.ConnectorLookup, item, target, lookup);
            try
            {
                return await ReconcileAsync(item, connection, attempt, target, lookup, cancellationToken);
            }
            catch (Exception exception)
            {
                AxisTelemetry.RecordFailure(lookupSpan, exception);
                throw;
            }
        }

        if (sending)
        {
            // The attempt the crashed worker made is already counted.
            return await SettleAttemptAsync(item, connection, attempt, target, ErrorClass.Unknown, null, UnknownOutcome, UnknownOutcome, cancellationToken);
        }

        using var attemptSpan = StartCallSpan(AxisTelemetry.ConnectorAttempt, item, target, target.Operation);
        try
        {
            return await SendAttemptAsync(item, connection, attempt, target, cancellationToken);
        }
        catch (Exception exception)
        {
            AxisTelemetry.RecordFailure(attemptSpan, exception);
            throw;
        }
    }

    /// <summary>Resolves the endpoint, sends the request and settles the call by the answer.</summary>
    private async Task<WorkItemOutcome> SendAttemptAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        Target target,
        CancellationToken cancellationToken)
    {
        var connector = target.Connector;
        if (ResolveEndpoint(item, connector, out var baseUrl, out var secret) is { } bindingError)
        {
            // The request is never sent.
            return await SettleAttemptAsync(item, connection, attempt, target, ErrorClass.Retryable, null, bindingError, bindingError, cancellationToken);
        }

        var answer = await SendAsync(target, attempt.Call, baseUrl, secret, cancellationToken);
        if (answer.Error is { } sendError)
        {
            return await SettleAttemptAsync(item, connection, attempt, target, answer.Class, answer.Status, sendError, answer.AttemptError!, cancellationToken);
        }

        if (Fit(answer.Body!, target) is not { } values)
        {
            // The system answered with success, so it may have acted. The call is never sent again.
            var misfit = $"The response did not fit the output of the operation '{target.Operation.Name}' of the connector '{connector.Name}'.";
            return await LeaveUnknownAsync(
                item,
                connection,
                attempt,
                target,
                answer.Status,
                misfit,
                AttemptRow(attempt, CallAttempts.Unknown, answer.Status, misfit),
                cancellationToken);
        }

        return await SucceedAsync(
            item,
            connection,
            attempt,
            target,
            answer.Status!.Value,
            values,
            AttemptRow(attempt, CallAttempts.Succeeded, answer.Status, null),
            cancellationToken);
    }

    /// <summary>
    /// The wait before the attempt after attempt <paramref name="attempts"/>: the policy's backoff,
    /// doubled for each attempt after the first, and at most an hour.
    /// </summary>
    internal static TimeSpan Backoff(RetryPolicyModel policy, int attempts)
    {
        ArgumentNullException.ThrowIfNull(policy);

        // The delay stays below the cap before it doubles, so it cannot overflow.
        var delay = policy.Backoff;
        for (var attempt = 1; attempt < attempts && delay < _maxBackoff; attempt++)
        {
            delay += delay;
        }

        return delay < _maxBackoff ? delay : _maxBackoff;
    }

    /// <summary>
    /// Asks the system about the <c>unknown</c> call through <paramref name="lookup"/>, with the
    /// operation identity as its key. A <c>200</c> whose body fits the output settles the call as
    /// succeeded. A <c>404</c> sets it back to <c>pending</c> for its next attempt, after the
    /// step's backoff, or fails it when no attempts are left. Anything else, and a lookup that is
    /// never sent, is a failed lookup that keeps the call <c>unknown</c>. A lookup counts no attempt.
    /// </summary>
    private async Task<WorkItemOutcome> ReconcileAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        Target target,
        ConnectorOperationModel lookup,
        CancellationToken cancellationToken)
    {
        var connector = target.Connector;
        if (ResolveEndpoint(item, connector, out var baseUrl, out var secret) is { } bindingError)
        {
            return await KeepUnknownAsync(item, connection, attempt, target, null, bindingError, LookupRow(attempt, CallAttempts.Failed, null, bindingError), cancellationToken);
        }

        var answer = await SendAsync(target with { Operation = lookup }, attempt.Call, baseUrl, secret, cancellationToken);
        if (answer is { Status: (int)HttpStatusCode.OK, Error: null } && Fit(answer.Body!, target) is { } values)
        {
            // The system acted on the call, so it is not sent again.
            return await SucceedAsync(
                item,
                connection,
                attempt,
                target,
                answer.Status.Value,
                values,
                LookupRow(attempt, CallAttempts.Found, answer.Status, null),
                cancellationToken);
        }

        if (answer.Status == (int)HttpStatusCode.NotFound)
        {
            // The system never acted, so the call is sent again under the same identity.
            var notFound = $"The lookup '{lookup.Name}' of the connector '{connector.Name}' found no call.";
            var policy = target.Step.Retry;
            var notFoundRow = LookupRow(attempt, CallAttempts.NotFound, answer.Status, notFound);
            return attempt.Attempts < policy.Attempts
                ? await RetryAsync(item, connection, attempt, answer.Status, notFound, Backoff(policy, attempt.Attempts), notFoundRow, cancellationToken)
                : await FailAsync(item, connection, attempt, answer.Status, $"{notFound} No attempts are left.", notFoundRow, cancellationToken);
        }

        var error = answer.Error
            ?? (answer.Status == (int)HttpStatusCode.OK
                ? $"The lookup '{lookup.Name}' of the connector '{connector.Name}' answered with a body that did not fit the output."
                : $"The lookup '{lookup.Name}' of the connector '{connector.Name}' answered with the status {answer.Status}.");
        return await KeepUnknownAsync(
            item,
            connection,
            attempt,
            target,
            answer.Status,
            error,
            LookupRow(attempt, CallAttempts.Failed, answer.Status, answer.AttemptError ?? error),
            cancellationToken);
    }

    /// <summary>
    /// Resolves the connector's base URL and auth secret from the tenant's bindings. Returns the
    /// error when a binding is missing or of the wrong kind, or when the URL is not <c>https</c> and
    /// the host does not allow <c>http</c>, or <see langword="null"/>.
    /// </summary>
    private string? ResolveEndpoint(ClaimedWorkItem item, ConnectorModel connector, out string baseUrl, out SecretValue? secret)
    {
        secret = null;
        try
        {
            baseUrl = bindings.Resolve(connector.BaseUrlBinding).Value;
            if (connector.Auth is { } auth)
            {
                secret = bindings.ResolveSecret(auth.Binding);
            }
        }
        catch (InvalidOperationException exception)
        {
            baseUrl = "";
            return exception.Message;
        }

        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || settings.AllowHttp)
            ? null
            : $"Tenant '{item.TenantId}' binding '{connector.BaseUrlBinding}' is not an https URL. Only Development allows http.";
    }

    /// <summary>The operation that looks up a call of the target's operation, or <see langword="null"/> when it declares none.</summary>
    private static ConnectorOperationModel? FindLookup(Target target) =>
        target.Operation.Lookup is { } name
            ? target.Connector.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            : null;

    private static Activity? StartCallSpan(string name, ClaimedWorkItem item, Target target, ConnectorOperationModel operation) =>
        AxisTelemetry.Source.StartActivity(
            name,
            ActivityKind.Internal,
            default(ActivityContext),
            [
                new(AxisTelemetry.TenantTag, item.TenantId),
                new(AxisTelemetry.ApplicationTag, target.Model.Manifest.Name),
                new(AxisTelemetry.ConnectorTag, target.Connector.Name),
                new(AxisTelemetry.OperationTag, operation.Name),
            ]);

    /// <summary>Finds the step, the subject entity, the connector and the operation of the call on its release.</summary>
    /// <exception cref="InvalidOperationException">The release lacks one of them.</exception>
    private async Task<Target> FindTargetAsync(WorkItemContext context, LoadedConnectorCall call, LoadedInstance instance, CancellationToken cancellationToken)
    {
        var model = await models.GetAsync(context, call.ReleaseId, cancellationToken);
        var process = model.Processes.FirstOrDefault(candidate => candidate.Id == instance.ProcessId)
            ?? throw new InvalidOperationException($"The release has no process '{instance.ProcessId}'.");
        var entity = model.FindEntity(instance.SubjectEntityId)
            ?? throw new InvalidOperationException($"The release has no entity '{instance.SubjectEntityId}'.");
        if (!process.TryGetStep(call.Step, out var step) || step is not CallConnectorStepModel callStep)
        {
            throw new InvalidOperationException($"The process '{process.Name}' has no callConnector step '{call.Step}'.");
        }

        var connector = model.Connectors.FirstOrDefault(candidate => string.Equals(candidate.Name, call.Connector, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The release has no connector '{call.Connector}'.");
        var operation = connector.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, call.Operation, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The connector '{connector.Name}' has no operation '{call.Operation}'.");
        return new Target(model, entity, callStep, connector, operation);
    }

    /// <summary>
    /// Sends the call's request and reads a <c>2xx</c> body. Any other status, a timeout or an
    /// exception is an error, with its class. An error names no header value and no body.
    /// </summary>
    private async Task<Answer> SendAsync(Target target, LoadedConnectorCall call, string baseUrl, SecretValue? secret, CancellationToken cancellationToken)
    {
        var connector = target.Connector;
        var operation = target.Operation;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(connector.Timeout);
        try
        {
            var url = baseUrl.TrimEnd('/') + operation.Path.Replace("{idempotencyKey}", call.Id.ToString("D"), StringComparison.Ordinal);
            using var request = new HttpRequestMessage(new HttpMethod(operation.Method), url);

            // The request says which operation it is for. Only scenario stubs read these options.
            request.Options.Set(ConnectorClient.ConnectorOption, connector.Name);
            request.Options.Set(ConnectorClient.OperationOption, operation.Name);
            if (request.Method != HttpMethod.Get)
            {
                request.Content = new StringContent(call.Input ?? "{}", Encoding.UTF8, "application/json");
            }

            if (connector.Auth is { } auth && secret is not null)
            {
                if (auth.Type == "bearer")
                {
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {secret.Reveal()}");
                }
                else
                {
                    request.Headers.TryAddWithoutValidation(auth.HeaderName!, secret.Reveal());
                }
            }

            if (connector.Idempotency is { } idempotency)
            {
                request.Headers.TryAddWithoutValidation(idempotency, call.Id.ToString("D"));
            }

            // The HTTP span shows the path template, never the address or the identity in the URL.
            if (Activity.Current is { } span && span.Source == AxisTelemetry.Source)
            {
                span.SetCustomProperty(AxisTelemetry.UrlTemplateProperty, operation.Path);
            }

            using var response = await client.SendAsync(request, timeout.Token);
            var status = (int)response.StatusCode;
            return response.IsSuccessStatusCode
                ? new Answer(status, await response.Content.ReadAsStringAsync(timeout.Token), null, ErrorClass.Failed)
                : new Answer(
                    status,
                    null,
                    $"The operation '{operation.Name}' of the connector '{connector.Name}' answered with the status {status}.",
                    Classify(status));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The request may have reached the system.
            return new Answer(
                null,
                null,
                $"The operation '{operation.Name}' of the connector '{connector.Name}' timed out after {connector.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.",
                ErrorClass.Unknown);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var message = exception.Message;
            if (secret?.Reveal() is { Length: > 0 } value)
            {
                message = message.Replace(value, SecretValue.Placeholder, StringComparison.Ordinal);
            }

            // Only a failed name lookup, connection or TLS handshake proves the request never left.
            var errorClass = exception is HttpRequestException
            {
                HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError,
            }
                ? ErrorClass.Retryable
                : ErrorClass.Unknown;
            // The exception message can hold a value, so an attempt row names only the exception type.
            return new Answer(
                null,
                null,
                $"The operation '{operation.Name}' of the connector '{connector.Name}' could not be called: {message}",
                errorClass,
                $"The operation '{operation.Name}' of the connector '{connector.Name}' could not be called: {exception.GetType().FullName}.");
        }
    }

    /// <summary>
    /// The class of an answer that is not a <c>2xx</c>. <c>408</c>, <c>429</c> and <c>503</c> are
    /// retryable, another <c>5xx</c> leaves the outcome unknown, and anything else fails at once.
    /// </summary>
    private static ErrorClass Classify(int status) =>
        status switch
        {
            (int)HttpStatusCode.RequestTimeout or (int)HttpStatusCode.TooManyRequests or (int)HttpStatusCode.ServiceUnavailable => ErrorClass.Retryable,
            >= 500 and < 600 => ErrorClass.Unknown,
            _ => ErrorClass.Failed,
        };

    /// <summary>
    /// Reads the outputs from a <c>2xx</c> body. The body fits when it is a JSON object with a value
    /// of the right type for each output it holds, and a value for each required output. Returns the
    /// subject fields to write with their values, or <see langword="null"/> when the body does not
    /// fit. An optional output the body leaves out writes nothing, and an explicit <c>null</c>
    /// writes <c>null</c>.
    /// </summary>
    private static List<KeyValuePair<FieldModel, object?>>? Fit(string body, Target target)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var values = new List<KeyValuePair<FieldModel, object?>>();
            foreach (var output in target.Operation.Output)
            {
                var present = root.TryGetProperty(output.Name, out var element);
                object? value = null;
                if (!present || element.ValueKind == JsonValueKind.Null)
                {
                    if (output.Required)
                    {
                        return null;
                    }
                }
                else if (!TryRead(element, output.Type, out value))
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

                    values.Add(KeyValuePair.Create(field, value));
                }
            }

            return values;
        }
    }

    /// <summary>
    /// Reads an output value in the form the expression interpreter gives, the reverse of how a
    /// staged input is written: text as a string, an integer as a long, a decimal as a decimal, a
    /// boolean as a bool, a date as a <see cref="DateOnly"/> and a date-time as a <see cref="DateTimeOffset"/>.
    /// </summary>
    internal static bool TryRead(JsonElement element, FieldType type, out object? value)
    {
        value = null;
        switch (type)
        {
            case FieldType.Text when element.ValueKind == JsonValueKind.String:
                value = element.GetString();
                return true;
            case FieldType.Integer when element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var integer):
                value = integer;
                return true;
            case FieldType.Decimal when element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number):
                value = number;
                return true;
            case FieldType.Boolean when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                value = element.GetBoolean();
                return true;
            case FieldType.Date when element.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(element.GetString(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date):
                value = date;
                return true;
            case FieldType.DateTime when element.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time):
                value = time.ToUniversalTime();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Records a fitting answer in one transaction: the output fields with the record update rules,
    /// the call <c>succeeded</c>, the instance <c>running</c> on <c>next</c>, the history row, the
    /// audit records, the next step's work item and the attempt row <paramref name="row"/>. A refused
    /// write or any other error rolls it back and fails the call instead, with a row that says the
    /// attempt failed, or for a lookup that it found the call.
    /// </summary>
    private async Task<WorkItemOutcome> SucceedAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        Target target,
        int status,
        List<KeyValuePair<FieldModel, object?>> values,
        CallAttempt row,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        string? failure;
        string? rowFailure;
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
                failure = await WriteSuccessAsync(item, transaction, instance!, attempt, target, status, values, row, cancellationToken);
                rowFailure = failure;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The message can hold record values, so the row names only the type.
                failure = exception.Message;
                rowFailure = $"The outcome of the call could not be recorded: {exception.GetType().FullName}.";
            }

            if (failure is null)
            {
                await transaction.CommitAsync(cancellationToken);
                LogSucceeded(call.Id, call.ProcessInstanceId, status);
                return WorkItemOutcome.Completed;
            }

            await transaction.RollbackAsync(cancellationToken);
        }

        var failedRow = row.Kind == CallAttempts.Lookup
            ? row with { Error = rowFailure }
            : row with { Outcome = CallAttempts.Failed, Error = rowFailure };
        return await FailAsync(item, connection, attempt, status, failure, failedRow, cancellationToken);
    }

    /// <summary>Writes the success in the transaction. Returns the error when the record write is refused, or <see langword="null"/>.</summary>
    private async Task<string?> WriteSuccessAsync(
        ClaimedWorkItem item,
        NpgsqlTransaction transaction,
        LoadedInstance instance,
        Attempt attempt,
        Target target,
        int status,
        List<KeyValuePair<FieldModel, object?>> values,
        CallAttempt row,
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
        var output = new JsonObject { ["operationId"] = call.Id, ["outcome"] = ConnectorCalls.Succeeded, ["next"] = target.Step.Next };
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

            await ComputedBackfill.MarkStaleWriteAsync(connection, transaction, item.TenantId, target.Model, entity, call.ReleaseId, cancellationToken);
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

        await ConnectorCalls.SettleAsync(transaction, call.Id, ConnectorCalls.Succeeded, status, null, cancellationToken);
        await CallAttempts.InsertAsync(transaction, CallAttempts.Connector, call.Id, row, cancellationToken);
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

        await AuditRecords.AppendAsync(transaction, Called(instance, attempt, ConnectorCalls.Succeeded, status), cancellationToken);
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
    /// connector also declares <c>idempotency</c>. An unknown outcome that is not retried leaves the
    /// call <c>unknown</c>, to be looked up when the operation declares a lookup. Anything else
    /// fails the call. The attempt row records the class of the attempt, with
    /// <paramref name="attemptError"/> as its error, whatever happens to the call.
    /// </summary>
    private Task<WorkItemOutcome> SettleAttemptAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        Target target,
        ErrorClass errorClass,
        int? status,
        string error,
        string attemptError,
        CancellationToken cancellationToken)
    {
        var policy = target.Step.Retry;
        var attemptsLeft = attempt.Attempts < policy.Attempts;
        var row = AttemptRow(
            attempt,
            errorClass switch
            {
                ErrorClass.Retryable => CallAttempts.Retryable,
                ErrorClass.Unknown => CallAttempts.Unknown,
                _ => CallAttempts.Failed,
            },
            status,
            attemptError);
        return errorClass switch
        {
            ErrorClass.Retryable when attemptsLeft =>
                RetryAsync(item, connection, attempt, status, error, Backoff(policy, attempt.Attempts), row, cancellationToken),
            ErrorClass.Unknown when attemptsLeft && target.Connector.Idempotency is not null =>
                RetryAsync(item, connection, attempt, status, error, Backoff(policy, attempt.Attempts), row, cancellationToken),
            ErrorClass.Unknown => LeaveUnknownAsync(item, connection, attempt, target, status, error, row, cancellationToken),
            _ => FailAsync(item, connection, attempt, status, error, row, cancellationToken),
        };
    }

    /// <summary>
    /// Sets the call back to <c>pending</c> with the attempt's status and error, and keeps the item,
    /// due again after <paramref name="delay"/>. The next attempt sends the stored input under the
    /// same operation identity. It writes <paramref name="row"/>, and no history row and no audit record.
    /// </summary>
    private async Task<WorkItemOutcome> RetryAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        int? status,
        string error,
        TimeSpan delay,
        CallAttempt row,
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

        await ConnectorCalls.RetryAsync(transaction, call.Id, status, error, cancellationToken);
        await CallAttempts.InsertAsync(transaction, CallAttempts.Connector, call.Id, row, cancellationToken);
        if (!await WorkItemQueue.RescheduleAsync(connection, transaction, item, delay, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, call.Id);
            return WorkItemOutcome.LostClaim;
        }

        await transaction.CommitAsync(cancellationToken);
        LogRetrying(call.Id, call.ProcessInstanceId, attempt.Attempts, status, error, delay);
        return WorkItemOutcome.Rescheduled;
    }

    /// <summary>
    /// Leaves the call <c>unknown</c> with the attempt's status and error, and keeps its stored input.
    /// The instance stays waiting. When the operation declares a lookup, the item is kept and due at
    /// once, so a worker asks the system what happened, and the history row waits until the call
    /// settles. Without one, the item is deleted, and a history row with the outcome <c>unknown</c>
    /// makes the call visible. It writes <paramref name="row"/>, and no audit record, because the
    /// call has not settled.
    /// </summary>
    private async Task<WorkItemOutcome> LeaveUnknownAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        Target target,
        int? status,
        string error,
        CallAttempt row,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        var lookup = FindLookup(target) is not null;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var instance = await ProcessSteps.LoadAsync(transaction, call.ProcessInstanceId, cancellationToken);
        var waiting = IsWaitingAt(instance, call, attempt);
        var claimed = waiting && lookup
            ? await WorkItemQueue.RescheduleAsync(connection, transaction, item, TimeSpan.Zero, cancellationToken)
            : await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken);
        if (!claimed)
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

        await ConnectorCalls.MarkUnknownAsync(transaction, call.Id, status, error, cancellationToken);
        await CallAttempts.InsertAsync(transaction, CallAttempts.Connector, call.Id, row, cancellationToken);
        if (lookup)
        {
            await transaction.CommitAsync(cancellationToken);
            LogLookingUp(call.Id, call.ProcessInstanceId, attempt.Attempts, status, error);
            return WorkItemOutcome.Rescheduled;
        }

        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(
                instance!.Id,
                call.Step,
                attempt.Revision,
                new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = null },
                new JsonObject { ["operationId"] = call.Id, ["outcome"] = ConnectorCalls.Unknown },
                null,
                error,
                attempt.StartedAt),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogUnknown(call.Id, instance.Id, attempt.Attempts, status, error);
        return WorkItemOutcome.Completed;
    }

    /// <summary>
    /// Keeps the <c>unknown</c> call after a failed lookup, with its status and error, and counts the
    /// failed lookup. The item is due again after the step's backoff, doubled for each failed lookup
    /// before this one. It writes the lookup row <paramref name="row"/>, and no history row and no
    /// audit record.
    /// </summary>
    private async Task<WorkItemOutcome> KeepUnknownAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        Target target,
        int? status,
        string error,
        CallAttempt row,
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

        var lookups = await ConnectorCalls.FailLookupAsync(transaction, call.Id, status, error, cancellationToken)
            ?? throw new InvalidOperationException($"The connector call '{call.Id}' is no longer unknown.");
        await CallAttempts.InsertAsync(transaction, CallAttempts.Connector, call.Id, row, cancellationToken);
        var delay = Backoff(target.Step.Retry, lookups);
        if (!await WorkItemQueue.RescheduleAsync(connection, transaction, item, delay, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, call.Id);
            return WorkItemOutcome.LostClaim;
        }

        await transaction.CommitAsync(cancellationToken);
        LogLookupFailed(call.Id, call.ProcessInstanceId, lookups, status, error, delay);
        return WorkItemOutcome.Rescheduled;
    }

    /// <summary>
    /// Settles the call <c>failed</c> with <paramref name="error"/> and fails its instance, with a
    /// history row and the audit records <c>connector.called</c> and <c>process.failed</c>. It writes
    /// <paramref name="row"/> unless it is <see langword="null"/>, which means that no attempt was made.
    /// </summary>
    private async Task<WorkItemOutcome> FailAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        int? status,
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

        await ConnectorCalls.SettleAsync(transaction, call.Id, ConnectorCalls.Failed, status, error, cancellationToken);
        if (row is not null)
        {
            await CallAttempts.InsertAsync(transaction, CallAttempts.Connector, call.Id, row, cancellationToken);
        }

        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(
                instance.Id,
                call.Step,
                attempt.Revision,
                new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = null },
                new JsonObject { ["operationId"] = call.Id, ["outcome"] = ConnectorCalls.Failed },
                null,
                error,
                attempt.StartedAt),
            cancellationToken);
        await AuditRecords.AppendAsync(transaction, Called(instance, attempt, ConnectorCalls.Failed, status), cancellationToken);
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
        LogFailed(call.Id, instance.Id, status, error);
        return WorkItemOutcome.Failed;
    }

    /// <summary>The row of the attempt the worker makes, numbered as the call counts it.</summary>
    private static CallAttempt AttemptRow(Attempt attempt, string outcome, int? status, string? error) =>
        new(CallAttempts.Attempt, attempt.Attempts, attempt.StartedAt, outcome, status, error);

    /// <summary>The row of a lookup. Its number is the next lookup number of the call.</summary>
    private static CallAttempt LookupRow(Attempt attempt, string outcome, int? status, string? error) =>
        new(CallAttempts.Lookup, null, attempt.StartedAt, outcome, status, error);

    private static bool IsWaitingAt(LoadedInstance? instance, LoadedConnectorCall call, Attempt attempt) =>
        instance is { State: ProcessStarts.Waiting } && instance.Step == call.Step && instance.Revision == attempt.Revision;

    private static AuditEntry Called(LoadedInstance instance, Attempt attempt, string outcome, int? status) =>
        new(
            AuditActors.System,
            AuditActions.ConnectorCalled,
            instance.ApplicationId,
            instance.SubjectEntityId,
            instance.SubjectId,
            instance.Id,
            new JsonObject
            {
                ["connector"] = attempt.Call.Connector,
                ["operation"] = attempt.Call.Operation,
                ["operationId"] = attempt.Call.Id,
                ["attempts"] = attempt.Attempts,
                ["outcome"] = outcome,
                ["lastStatus"] = status,
            });

    [LoggerMessage(LogLevel.Warning, "Connector call work item {ItemId} for call {CallId} was discarded: the call is {CallState} and its instance is {InstanceState}.")]
    private partial void LogDiscarded(Guid itemId, Guid? callId, string? callState, string? instanceState);

    [LoggerMessage(LogLevel.Warning, "Connector call work item {ItemId} for call {CallId} was claimed by another worker, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId, Guid? callId);

    [LoggerMessage(LogLevel.Warning, "Connector call {CallId} of process instance {InstanceId} was made at revision {Revision} of step {Step}, but the instance changed since, so its outcome was not recorded.")]
    private partial void LogStale(Guid callId, Guid instanceId, string step, long revision);

    [LoggerMessage(LogLevel.Information, "Connector call {CallId} of process instance {InstanceId} succeeded with the status {Status}.")]
    private partial void LogSucceeded(Guid callId, Guid instanceId, int status);

    [LoggerMessage(LogLevel.Warning, "Connector call {CallId} of process instance {InstanceId} failed with the status {Status}: {Error}")]
    private partial void LogFailed(Guid callId, Guid instanceId, int? status, string error);

    [LoggerMessage(LogLevel.Warning, "Connector call {CallId} of process instance {InstanceId} will be retried after {Delay}: attempt {Attempts} ended with the status {Status}: {Error}")]
    private partial void LogRetrying(Guid callId, Guid instanceId, int attempts, int? status, string error, TimeSpan delay);

    [LoggerMessage(LogLevel.Warning, "Connector call {CallId} of process instance {InstanceId} has an unknown outcome after {Attempts} attempts, with the status {Status}, and is not sent again: {Error}")]
    private partial void LogUnknown(Guid callId, Guid instanceId, int attempts, int? status, string error);

    [LoggerMessage(LogLevel.Warning, "Connector call {CallId} of process instance {InstanceId} has an unknown outcome after {Attempts} attempts, with the status {Status}, and is looked up: {Error}")]
    private partial void LogLookingUp(Guid callId, Guid instanceId, int attempts, int? status, string error);

    [LoggerMessage(LogLevel.Warning, "The lookup of connector call {CallId} of process instance {InstanceId} failed {Lookups} times in a row and will be tried again after {Delay}. The last ended with the status {Status}: {Error}")]
    private partial void LogLookupFailed(Guid callId, Guid instanceId, int lookups, int? status, string error, TimeSpan delay);

    /// <summary>
    /// The attempt a worker makes: the call, the instance revision it waits at, the attempts made
    /// with this one, and the time of the mark transaction, which starts the step's history row.
    /// </summary>
    private sealed record Attempt(LoadedConnectorCall Call, long Revision, int Attempts, DateTimeOffset StartedAt);

    /// <summary>What the call's release declares for it.</summary>
    private sealed record Target(
        ApplicationModel Model,
        EntityModel Entity,
        CallConnectorStepModel Step,
        ConnectorModel Connector,
        ConnectorOperationModel Operation);

    /// <summary>
    /// The status of an answered request, the body of a <c>2xx</c>, or the error of any other result
    /// with its class. <paramref name="RowError"/> replaces the error in an attempt row when the error
    /// holds an exception message.
    /// </summary>
    private sealed record Answer(int? Status, string? Body, string? Error, ErrorClass Class, string? RowError = null)
    {
        /// <summary>The error as an attempt row holds it, or <see langword="null"/> when there is none.</summary>
        public string? AttemptError => RowError ?? Error;
    }

    /// <summary>How an attempt that did not succeed is handled.</summary>
    private enum ErrorClass
    {
        /// <summary>The call fails at once.</summary>
        Failed,

        /// <summary>The request is known not to have acted, so the call is retried while attempts are left.</summary>
        Retryable,

        /// <summary>The system may have acted. The call is retried only with <c>idempotency</c>.</summary>
        Unknown,
    }
}
