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
/// extends the item's lease by the connector's timeout, or by four times the timeout for an <c>oauth2</c>
/// connector: a token request and a send, and the same two again after a <c>401</c>. The request then
/// carries the stored input where each input's <c>in</c> says (the path, the query, a header or the
/// JSON body), the auth from the tenant's secret binding and the operation identity as its idempotency key. An
/// <c>oauth2</c> connector sends the token that <see cref="IConnectorTokens"/> gets as a bearer token,
/// before each call or lookup. A <c>401</c> drops the cached token, gets a new one and sends the
/// request once more in the same attempt, with the same idempotency key, and a lookup does the same.
/// A second <c>401</c> is an answer like any other. A failed token request is a retryable attempt that is never sent, with
/// the status of the token endpoint, or a failed lookup, and it holds no body. An
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
/// value or token is stored or logged.
/// </summary>
internal sealed partial class ConnectorCallHandler(
    ReleaseModelCache models,
    IDeploymentBindings bindings,
    ConnectorClient client,
    IConnectorTokens tokens,
    ConnectorCallSettings settings,
    SensitiveValues? sensitive,
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
                attempt = attempt with { Application = target.Model.Manifest.Name };
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
                if (!await WorkItemQueue.ExtendLeaseAsync(connection, transaction, item, Lease(item, target.Connector), cancellationToken))
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
                if (!await WorkItemQueue.ExtendLeaseAsync(connection, transaction, item, Lease(item, target.Connector), cancellationToken))
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
        var endpoint = ResolveEndpoint(item, connector);
        if (endpoint.Error is { } bindingError)
        {
            // The request is never sent.
            return await SettleAttemptAsync(item, connection, attempt, target, ErrorClass.Retryable, null, bindingError, bindingError, cancellationToken);
        }

        var (tokenFailure, answer) = await SendWithTokenAsync(item, target, attempt.Call, endpoint, cancellationToken);
        if (tokenFailure is not null)
        {
            // The operation is never sent, and no body of the token endpoint is kept.
            return await SettleAttemptAsync(item, connection, attempt, target, ErrorClass.Retryable, tokenFailure.Status, tokenFailure.Error!, tokenFailure.Error!, cancellationToken);
        }

        if (answer!.Error is { } sendError)
        {
            return await SettleAttemptAsync(item, connection, attempt, target, answer.Class, answer.Status, sendError, answer.Error!, cancellationToken);
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
        var endpoint = ResolveEndpoint(item, connector);
        if (endpoint.Error is { } bindingError)
        {
            return await KeepUnknownAsync(item, connection, attempt, target, null, bindingError, LookupRow(attempt, CallAttempts.Failed, null, bindingError), cancellationToken);
        }

        var (tokenFailure, sent) = await SendWithTokenAsync(item, target with { Operation = lookup }, attempt.Call, endpoint, cancellationToken);
        if (tokenFailure is not null)
        {
            return await KeepUnknownAsync(
                item,
                connection,
                attempt,
                target,
                tokenFailure.Status,
                tokenFailure.Error!,
                LookupRow(attempt, CallAttempts.Failed, tokenFailure.Status, tokenFailure.Error),
                cancellationToken);
        }

        var answer = sent!;
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
            LookupRow(attempt, CallAttempts.Failed, answer.Status, answer.Error ?? error),
            cancellationToken);
    }

    /// <summary>
    /// Resolves the connector's base URL and auth secret from the tenant's bindings. For an
    /// <c>oauth2</c> auth it also resolves the token URL and the client id. The error is set when a
    /// binding is missing or of the wrong kind, or when the base URL or the token URL is not
    /// <c>https</c> and the host does not allow <c>http</c>.
    /// </summary>
    private Endpoint ResolveEndpoint(ClaimedWorkItem item, ConnectorModel connector)
    {
        string baseUrl;
        SecretValue? secret = null;
        string? tokenUrl = null;
        SecretValue? clientId = null;
        try
        {
            baseUrl = bindings.Resolve(connector.BaseUrlBinding).Value;
            if (connector.Auth is { } auth)
            {
                secret = bindings.ResolveSecret(auth.Binding);
                if (auth.OAuth2 is { } oauth)
                {
                    tokenUrl = bindings.Resolve(oauth.TokenUrlBinding).Value;
                    clientId = bindings.ResolveSecret(oauth.ClientIdBinding);
                }
            }
        }
        catch (InvalidOperationException exception)
        {
            return new Endpoint("", null, null, null, exception.Message);
        }

        if (!IsAllowed(baseUrl, out _))
        {
            return new Endpoint(baseUrl, secret, null, clientId, NotHttps(item, connector.BaseUrlBinding));
        }

        if (tokenUrl is null)
        {
            return new Endpoint(baseUrl, secret, null, clientId, null);
        }

        return IsAllowed(tokenUrl, out var uri)
            ? new Endpoint(baseUrl, secret, uri, clientId, null)
            : new Endpoint(baseUrl, secret, null, clientId, NotHttps(item, connector.Auth!.OAuth2!.TokenUrlBinding));
    }

    private bool IsAllowed(string url, out Uri? uri) =>
        Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttps || settings.AllowHttp);

    private static string NotHttps(ClaimedWorkItem item, string binding) =>
        $"Tenant '{item.TenantId}' binding '{binding}' is not an https URL. Only Development allows http.";

    /// <summary>
    /// Sends the request of <paramref name="target"/> with the connector's credential. An <c>oauth2</c>
    /// connector first gets its token. When the system answers <c>401</c>, the cached token is dropped, a
    /// new one is requested and the request is sent once more, with the same idempotency key. The result
    /// holds the failure of a token request, and then the request was not sent again, or the answer.
    /// </summary>
    private async Task<(ConnectorTokenResult? TokenFailure, Answer? Answer)> SendWithTokenAsync(
        ClaimedWorkItem item, Target target, LoadedConnectorCall call, Endpoint endpoint, CancellationToken cancellationToken)
    {
        var credential = endpoint.Secret;
        var issued = await GetTokenAsync(item, target.Connector, endpoint, cancellationToken);
        if (issued is not null)
        {
            if (issued.Error is not null)
            {
                return (issued, null);
            }

            credential = issued.Token;
        }

        var answer = await SendAsync(target, call, endpoint.BaseUrl, credential, cancellationToken);
        if (issued is null || answer.Status != (int)HttpStatusCode.Unauthorized)
        {
            return (null, answer);
        }

        tokens.Drop(item.TenantId, target.Connector, issued.Token!);
        var renewed = await GetTokenAsync(item, target.Connector, endpoint, cancellationToken);
        if (renewed!.Error is not null)
        {
            return (renewed, null);
        }

        return (null, await SendAsync(target, call, endpoint.BaseUrl, renewed.Token, cancellationToken));
    }

    /// <summary>
    /// Gets the access token of an <c>oauth2</c> connector, or gives <see langword="null"/> for a
    /// connector with another auth.
    /// </summary>
    private async Task<ConnectorTokenResult?> GetTokenAsync(ClaimedWorkItem item, ConnectorModel connector, Endpoint endpoint, CancellationToken cancellationToken) =>
        endpoint.TokenUrl is { } tokenUrl
            ? await tokens.GetAsync(item.TenantId, connector, tokenUrl, endpoint.ClientId!, endpoint.Secret!, cancellationToken)
            : null;

    /// <summary>
    /// How long the claim lasts while the call runs: the item's lease and the connector's timeout, and
    /// for an <c>oauth2</c> connector three more timeouts: the token request, and after a <c>401</c> a
    /// second token request and a second send.
    /// </summary>
    private static TimeSpan Lease(ClaimedWorkItem item, ConnectorModel connector) =>
        (connector.Auth?.OAuth2 is null ? connector.Timeout : connector.Timeout * 4) + item.Lease;

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

    /// <summary>
    /// Finds the step, the subject entity, the connector and the operation of the call on its release.
    /// A process without a subject has no entity.
    /// </summary>
    /// <exception cref="InvalidOperationException">The release lacks one of them.</exception>
    private async Task<Target> FindTargetAsync(WorkItemContext context, LoadedConnectorCall call, LoadedInstance instance, CancellationToken cancellationToken)
    {
        var model = await models.GetAsync(context, call.ReleaseId, cancellationToken);
        var process = model.Processes.FirstOrDefault(candidate => candidate.Id == instance.ProcessId)
            ?? throw new InvalidOperationException($"The release has no process '{instance.ProcessId}'.");
        var entity = instance.SubjectEntityId is { } entityId
            ? model.FindEntity(entityId) ?? throw new InvalidOperationException($"The release has no entity '{entityId}'.")
            : null;
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
    /// Sends the call's request and reads a <c>2xx</c> body. Each <c>path</c> input fills its
    /// placeholder, escaped as one segment. The <c>query</c> inputs make the query string and the
    /// <c>header</c> inputs the request headers, and an input with no value is left out. A non-<c>GET</c>
    /// sends a JSON body of the <c>body</c> inputs. Any other status, a timeout or an exception is an
    /// error, with its class. An error names no header value and no body.
    /// </summary>
    private async Task<Answer> SendAsync(Target target, LoadedConnectorCall call, string baseUrl, SecretValue? secret, CancellationToken cancellationToken)
    {
        var connector = target.Connector;
        var operation = target.Operation;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(connector.Timeout);
        try
        {
            var values = JsonNode.Parse(call.Input ?? "{}") as JsonObject ?? new JsonObject();
            var path = operation.Path.Replace("{idempotencyKey}", call.Id.ToString("D"), StringComparison.Ordinal);
            var query = new List<string>();
            var headers = new List<KeyValuePair<string, string>>();
            var body = new JsonObject();
            foreach (var property in values)
            {
                var field = operation.Input.FirstOrDefault(candidate => string.Equals(candidate.Name, property.Key, StringComparison.OrdinalIgnoreCase));
                if (field is null)
                {
                    continue;
                }

                if (field.In == "body")
                {
                    body[property.Key] = property.Value?.DeepClone();
                }
                else if (property.Value is not null)
                {
                    var text = property.Value is JsonValue value && value.TryGetValue<string>(out var textValue) ? textValue : property.Value.ToJsonString();
                    switch (field.In)
                    {
                        case "query":
                            query.Add($"{Uri.EscapeDataString(field.Name)}={Uri.EscapeDataString(text)}");
                            break;
                        case "header":
                            headers.Add(new KeyValuePair<string, string>(field.Name, text));
                            break;
                    }
                }
            }

            foreach (var field in operation.Input.Where(input => input.In == "path"))
            {
                var placeholder = $"{{{field.Name}}}";
                if (!path.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var pathValue = values.FirstOrDefault(property => string.Equals(property.Key, field.Name, StringComparison.OrdinalIgnoreCase)).Value;
                if (pathValue is null)
                {
                    // Compiling and staging prevent this. The error names the input, never a value.
                    return new Answer(
                        null,
                        null,
                        $"The operation '{operation.Name}' of the connector '{connector.Name}' has no value for the path input '{field.Name}'.",
                        ErrorClass.Failed);
                }

                var segment = pathValue is JsonValue pathScalar && pathScalar.TryGetValue<string>(out var pathText) ? pathText : pathValue.ToJsonString();
                path = path.Replace(placeholder, Uri.EscapeDataString(segment), StringComparison.OrdinalIgnoreCase);
            }

            if (query.Count > 0)
            {
                path += (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + string.Join('&', query);
            }

            var url = baseUrl.TrimEnd('/') + path;
            using var request = new HttpRequestMessage(new HttpMethod(operation.Method), url);

            // The request says which operation it is for. Only scenario stubs read these options.
            request.Options.Set(ConnectorClient.ConnectorOption, connector.Name);
            request.Options.Set(ConnectorClient.OperationOption, operation.Name);
            if (request.Method != HttpMethod.Get)
            {
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }

            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (connector.Auth is { } auth && secret is not null)
            {
                if (auth.Type is "bearer" or "oauth2")
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
            // Only a failed name lookup, connection or TLS handshake proves the request never left.
            var errorClass = exception is HttpRequestException
            {
                HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError,
            }
                ? ErrorClass.Retryable
                : ErrorClass.Unknown;
            // The exception message can hold a value, so the error names only the exception type.
            return new Answer(
                null,
                null,
                $"The operation '{operation.Name}' of the connector '{connector.Name}' could not be called: {exception.GetType().FullName}.",
                errorClass);
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
    /// Reads the outputs from a <c>2xx</c> body. An output is read from the place its <c>source</c>
    /// JSON Pointer names, or from the top-level property of its name when it has none. The body fits
    /// when it is a JSON object with a value of the right type for each output it holds, and a value for each required output. Returns the
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
                var present = output.Source is { } source
                    ? TryResolve(root, source, out var element)
                    : root.TryGetProperty(output.Name, out element);
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
                    if (target.Entity is not { } entity || !entity.TryGetField(mapping.Field, out var field))
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
    /// Finds the value a JSON Pointer (RFC 6901) names in <paramref name="root"/>. A token reads an
    /// object property by its exact name, or an array element by a decimal index without leading
    /// zeros. Returns false when the pointer names nothing.
    /// </summary>
    internal static bool TryResolve(JsonElement root, string pointer, out JsonElement element)
    {
        element = root;
        if (pointer.Length == 0)
        {
            return true;
        }

        if (pointer[0] != '/')
        {
            return false;
        }

        foreach (var encoded in pointer[1..].Split('/'))
        {
            var token = encoded.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            switch (element.ValueKind)
            {
                case JsonValueKind.Object when element.TryGetProperty(token, out var property):
                    element = property;
                    break;
                case JsonValueKind.Array when IsIndex(token) && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < element.GetArrayLength():
                    element = element[index];
                    break;
                default:
                    element = default;
                    return false;
            }
        }

        return true;
    }

    private static bool IsIndex(string token) =>
        token.Length > 0 && token.All(char.IsAsciiDigit) && (token.Length == 1 || token[0] != '0');

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
                // The message can hold record values, so the error names only the type.
                failure = $"The outcome of the call could not be recorded: {exception.GetType().FullName}.";
                rowFailure = failure;
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
        var cipher = sensitive is null ? null : new RecordCipher(sensitive, item.TenantId);
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

            record = await RecordQueries.GetAsync(connection, target.Model, entity, subjectId, cipher, cancellationToken);
            if (record is null && values.Count > 0)
            {
                return $"No record of '{entity.Name}' has the id '{subjectId}'.";
            }
        }
        else if (values.Count > 0)
        {
            return "The step has outputs, but the process has no subject to write them to.";
        }

        if (!await ProcessSteps.TryResumeCallAsync(transaction, instance.Id, call.Step, attempt.Revision, target.Step.Next, cancellationToken))
        {
            throw new InvalidOperationException($"The process instance '{instance.Id}' is no longer waiting on the step '{call.Step}'.");
        }

        var input = new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = record?.Version };
        var output = new JsonObject { ["operationId"] = call.Id, ["outcome"] = ConnectorCalls.Succeeded, ["next"] = target.Step.Next };
        AuditEntry? recordAudit = null;
        Record? updated = null;
        IReadOnlyList<EffectRecord> effects = [];
        var write = new RecordWriteContext(instance.AccountableUser, instance.DecisionTask);
        if (values.Count > 0)
        {
            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            var result = await RecordCommands.SetAsync(connection, target.Model, entity!, call.ReleaseId, write, record!, values, now, cipher: cipher, cancellationToken: cancellationToken);
            if (result is not { Outcome: RecordWriteOutcome.Written, Record: { } written })
            {
                return $"The record update was rejected: {ProcessStepHandler.Describe(result)}";
            }

            await ComputedBackfill.MarkStaleWriteAsync(connection, transaction, item.TenantId, target.Model, entity!, call.ReleaseId, cancellationToken);
            updated = written;
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

        await ConnectorCalls.SettleAsync(transaction, call.Id, ConnectorCalls.Succeeded, status, null, cancellationToken);
        await CallAttempts.InsertAsync(transaction, CallAttempts.Connector, call.Id, row, Tags(item, attempt), cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(instance.Id, call.Step, attempt.Revision, input, output, null, null, attempt.StartedAt),
            cancellationToken);
        if (recordAudit is not null)
        {
            await AuditRecords.AppendAsync(transaction, recordAudit, cancellationToken);
        }

        if (updated is not null)
        {
            var active = await models.FindActiveAsync(new WorkItemContext(item, connection, transaction), instance.ApplicationId, cancellationToken);
            if (active is { } activeNow)
            {
                await DataChangeTriggerStarts.StartAfterUpdateAsync(
                    transaction,
                    item.TenantId,
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
                item.TenantId,
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
        await CallAttempts.InsertAsync(transaction, CallAttempts.Connector, call.Id, row, Tags(item, attempt), cancellationToken);
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
        await CallAttempts.InsertAsync(transaction, CallAttempts.Connector, call.Id, row, Tags(item, attempt), cancellationToken);
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
        await CallAttempts.InsertAsync(transaction, CallAttempts.Connector, call.Id, row, Tags(item, attempt), cancellationToken);
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

        await InstanceMetrics.RecordEndedAsync(models, new WorkItemContext(item, connection, transaction), instance, ProcessStarts.Failed, cancellationToken);
        await ConnectorCalls.SettleAsync(transaction, call.Id, ConnectorCalls.Failed, status, error, cancellationToken);
        if (row is not null)
        {
            await CallAttempts.InsertAsync(transaction, CallAttempts.Connector, call.Id, row, Tags(item, attempt), cancellationToken);
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

    private static CallTags Tags(ClaimedWorkItem item, Attempt attempt) =>
        new(item.TenantId, attempt.Application, AxisTelemetry.ConnectorTag, attempt.Call.Connector, attempt.Call.Operation);

    /// <summary>The row of the attempt the worker makes. Its number is the next attempt number of the call, which carries on after an operator retry.</summary>
    private static CallAttempt AttemptRow(Attempt attempt, string outcome, int? status, string? error) =>
        new(CallAttempts.Attempt, null, attempt.StartedAt, outcome, status, error);

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
    private sealed record Attempt(LoadedConnectorCall Call, long Revision, int Attempts, DateTimeOffset StartedAt, string? Application = null);

    /// <summary>
    /// The connector's resolved base URL and auth secret, and for an <c>oauth2</c> auth its token URL
    /// and client id. <see cref="Error"/> is set when a binding fails the checks.
    /// </summary>
    private sealed record Endpoint(string BaseUrl, SecretValue? Secret, Uri? TokenUrl, SecretValue? ClientId, string? Error);

    /// <summary>What the call's release declares for it.</summary>
    private sealed record Target(
        ApplicationModel Model,
        EntityModel? Entity,
        CallConnectorStepModel Step,
        ConnectorModel Connector,
        ConnectorOperationModel Operation);

    /// <summary>
    /// The status of an answered request, the body of a <c>2xx</c>, or the error of any other result
    /// with its class. An error holds no exception message, because a message can hold a value.
    /// </summary>
    private sealed record Answer(int? Status, string? Body, string? Error, ErrorClass Class);

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
