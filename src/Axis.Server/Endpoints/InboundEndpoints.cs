using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Axis.Configuration.Model;
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
/// A call is checked in order: bearer token (401), application and endpoint (404), the caller's
/// <c>invoke</c> grant on the endpoint (403), content type (415), and the body against the
/// endpoint's input fields (400 keyed <c>/input/…</c>). One transaction then creates the record
/// from the mapped values through the record create command, with the caller's id in its creator
/// fields, and starts the endpoint's process on it as the process start does. A refused value, a
/// failed validation and a missing reference are a 400 keyed <c>/values/…</c>, and a failed start
/// condition a 400 keyed <c>/input</c>. Both audit records name the caller as the actor and the
/// endpoint in their details. A call that fails writes nothing. The entity and process grants of
/// the caller are not checked: the create and the start are the endpoint's contract.
/// After the commit, an endpoint with <c>output</c> and <c>wait</c> checks the instance's state
/// until it completes, fails or <c>wait</c> runs out, holding no connection between checks. A
/// completed instance is a <c>201</c> with the output fields, and any other a <c>202</c> with the
/// status location in <c>Location</c>.
/// The status location is checked in the same order up to the grant, then answers <c>404</c> for
/// an instance that this endpoint did not start for this caller, as its <c>process.started</c>
/// audit record says. Otherwise it is a <c>200</c> with the instance's state, and the output
/// fields once it completed. Output values are the record as stored, outside field grants.
/// </summary>
internal static class InboundEndpoints
{
    private const string InputPointer = "/input";

    // The worker polls once a second, so checking more often gains little.
    private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(200);

    public static void MapInboundEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/apps/{app}/endpoints/{name}", CallAsync);
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
        CancellationToken cancellationToken)
    {
        if (await FindCallerAsync(request) is not { } caller)
        {
            return Unauthorized(request);
        }

        // An unknown application and an unknown endpoint get the same answer: a caller has no need
        // to tell them apart.
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

        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        var parsed = RecordInputParser.ParseEndpointInput(await RecordEndpoints.ReadBodyAsync(request, cancellationToken), endpoint, application);
        if (parsed.Errors is { } errors)
        {
            return Results.ValidationProblem(errors);
        }

        var entity = application.FindEntity(endpoint.Entity.Id)
            ?? throw new InvalidOperationException($"The entity '{endpoint.Entity.Name}' of endpoint '{endpoint.Name}' is not in the model.");
        if (!application.TryGetProcess(endpoint.Process.Name, out var process))
        {
            throw new InvalidOperationException($"The process '{endpoint.Process.Name}' of endpoint '{endpoint.Name}' is not in the model.");
        }

        var mapped = EndpointInputMapper.Map(application, entity, endpoint, parsed.Input!);
        if (mapped.Errors is { } mapErrors)
        {
            return Results.ValidationProblem(mapErrors);
        }

        // The creator fields are set before the computed fields and validations, as in a record API
        // create, so they see the record as it will be stored.
        var values = RecordCreator.WithCreator(entity, mapped.Values, caller.Id);
        var computed = RecordComputer.ComputeCreate(application, entity, values, mapped.Rows);
        if (computed.Errors is { } computeErrors)
        {
            return Results.ValidationProblem(computeErrors);
        }

        var tenantId = (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;
        var details = new JsonObject { ["endpoint"] = endpoint.Name };
        var instanceId = Guid.CreateVersion7();
        var connection = await database.GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The validations run in the write transaction, so their now() is its start time.
        var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        if (RecordValidator.ValidateCreate(application, entity, computed.Values, computed.Rows, now) is { } failures)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.ValidationProblem(failures);
        }

        var result = await RecordCommands.CreateAsync(connection, application, entity, computed.Values, computed.Rows, cancellationToken: cancellationToken);
        if (result is not { Outcome: RecordWriteOutcome.Written, Record: { } record })
        {
            await transaction.RollbackAsync(cancellationToken);
            return RecordEndpoints.WriteFailure(result);
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
                new JsonObject { ["version"] = record.Version, ["endpoint"] = endpoint.Name },
                caller.DisplayName),
            cancellationToken);

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
            instanceId,
            caller.Id,
            caller.DisplayName,
            details,
            cancellationToken);
        if (started.Outcome != ProcessStartOutcome.Started)
        {
            await transaction.RollbackAsync(cancellationToken);
            return started.Outcome == ProcessStartOutcome.ConditionFailed
                ? Results.ValidationProblem(new Dictionary<string, string[]> { [InputPointer] = [started.TextKey!] })
                : ProcessStarter.AlreadyRunning();
        }

        await transaction.CommitAsync(cancellationToken);
        if (endpoint.Wait is not { } wait)
        {
            return Results.Json(new EndpointCallResponse(record.Id, instanceId, ProcessStarts.Running), statusCode: StatusCodes.Status201Created);
        }

        await transaction.DisposeAsync();
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
                var output = await ReadOutputAsync(polled, application, entity, endpoint, record.Id, cancellationToken);
                await database.ReleaseConnectionAsync();
                return Results.Json(new EndpointCallResponse(record.Id, instanceId, state, output), statusCode: StatusCodes.Status201Created);
            }

            await database.ReleaseConnectionAsync();

            // A failed instance never completes, so waiting longer has no use.
            if (state == ProcessStarts.Failed)
            {
                break;
            }
        }

        return Results.Accepted(StatusLocation(application, endpoint, instanceId), new EndpointCallResponse(record.Id, instanceId, state));
    }

    // It takes the request rather than the context, so it binds as a route handler and its result is written.
    private static async Task<IResult> StatusAsync(
        string app,
        string name,
        string instanceId,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
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

        if (instanceId.Length != 36 || !Guid.TryParseExact(instanceId, "D", out var id))
        {
            return InstanceNotFound();
        }

        // Another caller's instance, another endpoint's and an unknown one get the same answer, so
        // one system cannot learn of another system's calls.
        var connection = await database.GetConnectionAsync(cancellationToken);
        if (await ProcessStarts.FindInstanceAsync(connection, id, cancellationToken) is not { } instance
            || instance.ApplicationId != application.Manifest.Id
            || instance.ProcessId != endpoint.Process.Id
            || instance.SubjectEntityId != endpoint.Entity.Id
            || !await AuditRecords.IsProcessStartedByAsync(
                connection, instance.SubjectEntityId, instance.SubjectId, id, caller.Id, endpoint.Name, cancellationToken))
        {
            return InstanceNotFound();
        }

        var output = instance.State == ProcessStarts.Completed && application.FindEntity(endpoint.Entity.Id) is { } entity
            ? await ReadOutputAsync(connection, application, entity, endpoint, instance.SubjectId, cancellationToken)
            : null;
        return Results.Ok(new EndpointCallResponse(instance.SubjectId, id, instance.State, output));
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
        CancellationToken cancellationToken)
    {
        if (endpoint.Output is not { } fields
            || await RecordQueries.GetAsync(connection, application, entity, recordId, cancellationToken) is not { } record)
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

    private static IResult Unauthorized(HttpRequest request)
    {
        request.HttpContext.Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "A valid bearer token is needed to call this endpoint.");
    }

    private static IResult NotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No endpoint exists with this name.");

    private static IResult InstanceNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No instance of this endpoint exists with this id.");
}

/// <summary>
/// The record and the instance an endpoint call created, the instance's state, and the endpoint's
/// output fields once the instance completed.
/// </summary>
internal sealed record EndpointCallResponse(
    Guid RecordId,
    Guid InstanceId,
    string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonObject? Output = null);
