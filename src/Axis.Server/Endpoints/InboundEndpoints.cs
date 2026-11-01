using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Core.Tenancy;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Instances;
using Axis.Server.Applications;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Processes;
using Axis.Server.Records;
using Axis.Server.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Npgsql;

namespace Axis.Server.Endpoints;

/// <summary>
/// The routes other systems call with a bearer token: the endpoint call and its status location.
/// They take no session and no CSRF header. A request without a valid token is a <c>401</c> with
/// <c>WWW-Authenticate: Bearer</c>, which never says which check failed.
/// The transaction takes the application's start lock shared first. When the active release is no
/// longer the one resolved, the call runs again from resolution on the new release, and answers 503
/// after three attempts. The transaction runs under <c>Data:WriteTimeout</c>, set before the start lock. A call past it
/// rolls back, writes nothing and answers 503.
/// A call is checked in order: bearer token (401), application and endpoint (404), the caller's
/// <c>invoke</c> grant on the endpoint (403), content type (415), the body against the
/// endpoint's input fields (400 keyed <c>/input/…</c>), and <c>Idempotency-Key</c> (400). One
/// transaction then claims the key's receipt, unique per endpoint and caller, and creates the record
/// from the mapped values through the record create command, with the caller's id in its creator
/// fields, starts the processes of the data change triggers on the entity's create as <c>system</c>,
/// and starts the endpoint's process on it as the process start does. An endpoint with an
/// <c>event</c> then raises it for the new record in the same transaction, as a <c>raiseEvent</c>
/// step does, with no instance when the endpoint has no process. Such an endpoint answers a <c>201</c>
/// with the record's id only, as soon as the transaction commits, and its status location is always a
/// <c>404</c>. A <c>fields</c> expression that fails at run time is a 400 keyed
/// <c>/event/fields/…</c>. A refused value, a
/// failed validation and a missing reference are a 400 keyed <c>/values/…</c>, and a failed start
/// condition a 400 keyed <c>/input</c>. Both audit records name the caller as the actor and the
/// endpoint in their details. A call that fails writes nothing. The entity and process grants of
/// the caller are not checked: the create and the start are the endpoint's contract.
/// After the commit, an endpoint with <c>output</c> and <c>wait</c> checks the instance's state
/// until it completes, fails or <c>wait</c> runs out, holding no connection between checks. A
/// completed instance is a <c>201</c> with the output fields, and any other a <c>202</c> with the
/// status location in <c>Location</c>.
/// A repeat with a claimed key and the same input creates nothing and answers from the receipt with
/// the instance's state now, without waiting. The same key with another input is a <c>422</c>.
/// The status location is checked in the same order up to the grant, then answers <c>404</c> for
/// an instance that this endpoint did not start for this caller, as its <c>process.started</c>
/// audit record says. Otherwise it is a <c>200</c> with the instance's state, and the output
/// fields once it completed. Output values are the record as stored, outside field grants.
/// </summary>
internal static class InboundEndpoints
{
    private const string InputProperty = "input";

    private const string InputPointer = "/" + InputProperty;

    // The worker polls once a second, so checking more often gains little.
    private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(200);

    public static void MapInboundEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/apps/{app}/endpoints/{name}", CallAsync).WithWriteTimeout();
        endpoints.MapGet("/api/apps/{app}/endpoints/{name}/instances/{instanceId}", StatusAsync);
    }

    // The body is read here rather than bound, so the content type is checked before any of the
    // body is read and the parser reports every body problem by JSON Pointer.
    private static async Task<IResult> CallAsync(
        string app,
        string name,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        SensitiveValues sensitive,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await FindCallerAsync(request) is not { } caller)
        {
            return Unauthorized(request);
        }

        // A release change under the start lock sends the call back to resolution, so every check runs on the new release.
        for (var attempt = 0; attempt < ReleaseRetry.MaxAttempts; attempt++)
        {
            var call = await CallOnceAsync(app, name, request, caller, resolver, database, tenants, sensitive, loggerFactory, cancellationToken);
            if (call.Committed is not { } committed)
            {
                if (call.Result is { } result)
                {
                    return result;
                }

                continue;
            }

            return await WaitAsync(database, committed, cancellationToken);
        }

        return ReleaseRetry.Changed();
    }

    /// <summary>
    /// One attempt of a call, up to its commit. It has a result for an answer, and a
    /// <see cref="CommittedCall"/> once the transaction committed. It has neither when the active
    /// release changed while the call waited for the start lock, and nothing was written.
    /// </summary>
    private sealed record CallAttempt(IResult? Result = null, CommittedCall? Committed = null);

    /// <summary>The call as committed, for the wait that follows.</summary>
    private sealed record CommittedCall(ApplicationModel Application, EntityModel Entity, EndpointModel Endpoint, Guid RecordId, Guid InstanceId, RecordCipher Cipher);

    private static async Task<CallAttempt> CallOnceAsync(
        string app,
        string name,
        HttpRequest request,
        EndpointCallerIdentity caller,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        SensitiveValues sensitive,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // An unknown application and an unknown endpoint get the same answer: a caller has no need
        // to tell them apart.
        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active
            || !active.Model.TryGetEndpoint(name, out var endpoint))
        {
            return new(NotFound());
        }

        var application = active.Model;
        if (!PolicyEvaluator.AllowsInvoke(application, caller.Roles, endpoint))
        {
            return new(Grants.Forbidden());
        }

        if (!JsonRequest.IsJson(request))
        {
            return new(JsonRequest.UnsupportedMediaType());
        }

        var body = await RecordEndpoints.ReadBodyAsync(request, cancellationToken);
        var parsed = RecordInputParser.ParseEndpointInput(body, endpoint, application);
        if (parsed.Errors is { } errors)
        {
            return new(Results.ValidationProblem(errors));
        }

        if (!ProcessStartEndpoints.TryReadIdempotencyKey(request.Headers[ProcessStartEndpoints.IdempotencyKeyHeader], out var key))
        {
            return new(ProcessStartEndpoints.IdempotencyKeyProblem());
        }

        var entity = application.FindEntity(endpoint.Entity.Id)
            ?? throw new InvalidOperationException($"The entity '{endpoint.Entity.Name}' of endpoint '{endpoint.Name}' is not in the model.");
        ProcessModel? process = null;
        if (endpoint.Process is { } processReference && !application.TryGetProcess(processReference.Name, out process))
        {
            throw new InvalidOperationException($"The process '{processReference.Name}' of endpoint '{endpoint.Name}' is not in the model.");
        }

        var mapped = EndpointInputMapper.Map(application, entity, endpoint, parsed.Input!);
        if (mapped.Errors is { } mapErrors)
        {
            return new(Results.ValidationProblem(mapErrors));
        }

        var tenantId = (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;
        var cipher = new RecordCipher(sensitive, tenantId);
        var details = new JsonObject { ["endpoint"] = endpoint.Name };
        var recordId = Guid.CreateVersion7();
        Guid? instanceId = process is null ? null : Guid.CreateVersion7();
        var connection = await database.GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The write limit comes first, so it also bounds the wait for the start lock.
        await database.LimitWriteAsync(transaction, cancellationToken);

        // The start lock comes first, before any entity table is touched, so a migrating activation
        // cannot deadlock with this call. The release is read again under it.
        if (await StartLock.TakeSharedAsync(transaction, application.Manifest.Id, cancellationToken) != active.ReleaseId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new();
        }

        // The receipt is claimed first, so a repeat answers from it even when a validation would
        // now refuse the input, and a concurrent repeat waits on the receipt's key. The parser
        // has checked the body, so its input is an object.
        if (key is not null)
        {
            var input = JsonNode.Parse(body)![InputProperty]!.ToJsonString();
            if (!await EndpointReceipts.TryClaimAsync(
                transaction, application.Manifest.Id, endpoint.Id, caller.Id, key, recordId, instanceId, input, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                var receipt = await EndpointReceipts.FindAsync(
                    connection, application.Manifest.Id, endpoint.Id, caller.Id, key, input, cancellationToken)
                    ?? throw new InvalidOperationException("The receipt of a claimed Idempotency-Key could not be found.");
                return new(await RepeatAsync(connection, application, entity, endpoint, receipt, cipher, cancellationToken));
            }
        }

        // The defaults, the computed fields and the validations run in the write transaction, so their
        // now() is its start time. The defaults and the creator fields are set first, as in a record API
        // create, so the computed fields and validations see the record as it will be stored.
        var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        var defaults = RecordDefaults.Apply(entity, mapped.Values, now, application.TimeZone);
        if (defaults.Errors is { } defaultErrors)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(Results.ValidationProblem(defaultErrors));
        }

        var values = RecordCreator.WithCreator(entity, defaults.Values, caller.Id);
        var computed = RecordComputer.ComputeCreate(application, entity, values, mapped.Rows);
        if (computed.Errors is { } computeErrors)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(Results.ValidationProblem(computeErrors));
        }

        if (RecordValidator.ValidateCreate(application, entity, computed.Values, computed.Rows, now) is { } failures)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(Results.ValidationProblem(failures));
        }

        var result = await RecordCommands.CreateAsync(connection, application, entity, active.ReleaseId, computed.Values, computed.Rows, id: recordId, cipher: cipher, cancellationToken: cancellationToken);
        if (result is not { Outcome: RecordWriteOutcome.Written, Record: { } record })
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(RecordEndpoints.WriteFailure(result));
        }

        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                caller.Id,
                AuditActions.RecordCreated,
                application.Manifest.Id,
                entity.Id,
                record.Id,
                ProcessInstanceId: null,
                new JsonObject { ["version"] = record.Version, ["endpoint"] = endpoint.Name }),
            cancellationToken);

        // The data change triggers of the create fire before the endpoint's own start, as system.
        await DataChangeTriggerStarts.StartAsync(
            transaction,
            tenantId,
            application,
            active.ReleaseId,
            entity,
            DataChangeKinds.Create,
            record,
            changedFields: null,
            caller.Id,
            loggerFactory.CreateLogger(typeof(DataChangeTriggerStarts)),
            cancellationToken);

        if (process is not null)
        {
            var started = await ProcessStarter.StartAsync(
                connection,
                transaction,
                tenantId,
                application,
                active.ReleaseId,
                process,
                entity,
                record.Id,
                subject: null,
                instanceId!.Value,
                caller.Id,
                caller.Id,
                details,
                cancellationToken);
            if (started.Outcome != ProcessStartOutcome.Started)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new(started.Outcome == ProcessStartOutcome.ConditionFailed
                    ? Results.ValidationProblem(new Dictionary<string, string[]> { [InputPointer] = [started.TextKey!] })
                    : ProcessStartEndpoints.AlreadyRunning());
            }
        }

        // The event is raised after the start, so a failed start condition and a running instance are
        // answered before an event error. Its fields see the transaction's time as now().
        if (endpoint.Event is { } raised)
        {
            var mappedEvent = EndpointInputMapper.MapEvent(application, endpoint, parsed.Input!, now);
            if (mappedEvent.Errors is { } eventErrors)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new(Results.ValidationProblem(eventErrors));
            }

            var @event = application.Events.FirstOrDefault(candidate => string.Equals(candidate.Name, raised.Name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"The event '{raised.Name}' of endpoint '{endpoint.Name}' is not in the model.");
            await EventRaiser.RaiseAsync(
                transaction,
                tenantId,
                application,
                active.ReleaseId,
                @event,
                Guid.CreateVersion7(),
                record.Id,
                EventRaiser.Payload(mappedEvent.Fields),
                instanceId,
                (active.ReleaseId, application),
                loggerFactory.CreateLogger(typeof(EventRaiser)),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // An endpoint without a process has no instance to wait for, and its schema refuses a wait.
        if (instanceId is not { } startedInstanceId)
        {
            return new(Results.Json(new EndpointCallResponse(record.Id), statusCode: StatusCodes.Status201Created));
        }

        return endpoint.Wait is null
            ? new(Results.Json(new EndpointCallResponse(record.Id, startedInstanceId, ProcessStarts.Running), statusCode: StatusCodes.Status201Created))
            : new(Committed: new CommittedCall(application, entity, endpoint, record.Id, startedInstanceId, cipher));
    }

    // Holds no connection between checks. The transaction of the attempt is already disposed.
    private static async Task<IResult> WaitAsync(TenantDatabase database, CommittedCall call, CancellationToken cancellationToken)
    {
        var (application, entity, endpoint, recordId, instanceId, cipher) = call;
        var wait = endpoint.Wait!.Value;
        await database.ReleaseConnectionAsync();
        var state = ProcessStarts.Running;
        var waitStart = Stopwatch.GetTimestamp();
        for (var remaining = wait; remaining > TimeSpan.Zero; remaining = wait - Stopwatch.GetElapsedTime(waitStart))
        {
            await Task.Delay(remaining < _pollInterval ? remaining : _pollInterval, cancellationToken);
            var polled = await database.GetConnectionAsync(cancellationToken);
            state = (await ProcessStarts.FindInstanceAsync(polled, instanceId, cancellationToken))?.State ?? state;
            if (state == ProcessStarts.Completed)
            {
                var output = await ReadOutputAsync(polled, application, entity, endpoint, recordId, cipher, cancellationToken);
                await database.ReleaseConnectionAsync();
                return Results.Json(new EndpointCallResponse(recordId, instanceId, state, output), statusCode: StatusCodes.Status201Created);
            }

            await database.ReleaseConnectionAsync();

            // A failed instance never completes, so waiting longer has no use.
            if (state == ProcessStarts.Failed)
            {
                break;
            }
        }

        return Results.Accepted(StatusLocation(application, endpoint, instanceId), new EndpointCallResponse(recordId, instanceId, state));
    }

    /// <summary>
    /// The answer to a repeat of a claimed key, built from its receipt and the instance's state now.
    /// A repeat never waits. An input other than the first call's is a <c>422</c>.
    /// </summary>
    private static async Task<IResult> RepeatAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        EndpointModel endpoint,
        EndpointReceipt receipt,
        RecordCipher cipher,
        CancellationToken cancellationToken)
    {
        if (!receipt.SameInput)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "This idempotency key was used with a different input.");
        }

        if (receipt.InstanceId is not { } instanceId)
        {
            return Results.Json(new EndpointCallResponse(receipt.RecordId), statusCode: StatusCodes.Status201Created);
        }

        var state = (await ProcessStarts.FindInstanceAsync(connection, instanceId, cancellationToken)
            ?? throw new InvalidOperationException("The instance of an endpoint receipt could not be found.")).State;
        if (endpoint.Output is null)
        {
            return Results.Json(new EndpointCallResponse(receipt.RecordId, instanceId, state), statusCode: StatusCodes.Status201Created);
        }

        if (state == ProcessStarts.Completed)
        {
            var output = await ReadOutputAsync(connection, application, entity, endpoint, receipt.RecordId, cipher, cancellationToken);
            return Results.Json(new EndpointCallResponse(receipt.RecordId, instanceId, state, output), statusCode: StatusCodes.Status201Created);
        }

        return Results.Accepted(
            StatusLocation(application, endpoint, instanceId),
            new EndpointCallResponse(receipt.RecordId, instanceId, state));
    }

    // It takes the request rather than the context, so it binds as a route handler and its result is written.
    private static async Task<IResult> StatusAsync(
        string app,
        string name,
        string instanceId,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        SensitiveValues sensitive,
        ITenantContextAccessor tenants,
        CancellationToken cancellationToken)
    {
        if (await FindCallerAsync(request) is not { } caller)
        {
            return Unauthorized(request);
        }

        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active
            || !active.Model.TryGetEndpoint(name, out var endpoint))
        {
            return NotFound();
        }

        var application = active.Model;
        if (!PolicyEvaluator.AllowsInvoke(application, caller.Roles, endpoint))
        {
            return Grants.Forbidden();
        }

        // An endpoint without a process starts no instance, so every id is unknown to it.
        if (endpoint.Process is not { } process || instanceId.Length != 36 || !Guid.TryParseExact(instanceId, "D", out var id))
        {
            return InstanceNotFound();
        }

        // Another caller's instance, another endpoint's and an unknown one get the same answer, so
        // one system cannot learn of another system's calls.
        var connection = await database.GetConnectionAsync(cancellationToken);
        if (await ProcessStarts.FindInstanceAsync(connection, id, cancellationToken) is not { SubjectEntityId: { } subjectEntityId, SubjectId: { } subjectId } instance
            || instance.ApplicationId != application.Manifest.Id
            || instance.ProcessId != process.Id
            || subjectEntityId != endpoint.Entity.Id
            || !await AuditRecords.IsProcessStartedByAsync(
                connection, subjectEntityId, subjectId, id, caller.Id, endpoint.Name, cancellationToken))
        {
            return InstanceNotFound();
        }

        var output = instance.State == ProcessStarts.Completed && application.FindEntity(endpoint.Entity.Id) is { } entity
            ? await ReadOutputAsync(connection, application, entity, endpoint, subjectId, RecordEndpoints.Cipher(sensitive, tenants), cancellationToken)
            : null;
        return Results.Ok(new EndpointCallResponse(subjectId, id, instance.State, output));
    }

    /// <summary>
    /// The endpoint's output fields of the record as stored, in declaration order, or null when the
    /// endpoint declares no output or the record is gone.
    /// </summary>
    private static async Task<JsonObject?> ReadOutputAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        EndpointModel endpoint,
        Guid recordId,
        RecordCipher cipher,
        CancellationToken cancellationToken)
    {
        if (endpoint.Output is not { } fields
            || await RecordQueries.GetAsync(connection, application, entity, recordId, cipher, cancellationToken) is not { } record)
        {
            return null;
        }

        var output = new JsonObject();
        foreach (var field in fields)
        {
            output[field] = record.Values.TryGetValue(field, out var value) ? value?.DeepClone() : null;
        }

        return output;
    }

    // The declared names, not the request's letter case, give the caller a stable address.
    private static string StatusLocation(ApplicationModel application, EndpointModel endpoint, Guid instanceId) =>
        $"/api/apps/{application.Manifest.Name}/endpoints/{endpoint.Name}/instances/{instanceId:D}";

    private static async Task<EndpointCallerIdentity?> FindCallerAsync(HttpRequest request)
    {
        var caller = await request.HttpContext.AuthenticateAsync(EndpointCaller.Scheme);
        return caller.Succeeded ? EndpointCaller.Read(caller.Principal) : null;
    }

    private static IResult Unauthorized(HttpRequest request) => Grants.EndpointTokenRequired(request.HttpContext);

    private static IResult NotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No endpoint exists with this name.");

    private static IResult InstanceNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No instance of this endpoint exists with this id.");
}

/// <summary>
/// The record and the instance an endpoint call created, the instance's state, and the endpoint's
/// output fields once the instance completed. An endpoint without a process starts no instance, so
/// its answer has the record only.
/// </summary>
internal sealed record EndpointCallResponse(
    Guid RecordId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? InstanceId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? State = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonObject? Output = null);
