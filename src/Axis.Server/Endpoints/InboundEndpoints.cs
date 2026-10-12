using System.Text.Json.Nodes;
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
/// </summary>
internal static class InboundEndpoints
{
    private const string InputPointer = "/input";

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
        return Results.Json(new EndpointCallResponse(record.Id, instanceId, ProcessStarts.Running), statusCode: StatusCodes.Status201Created);
    }

    // It takes the request rather than the context, so it binds as a route handler and its result is written.
    // The status location issue replaces the 404 with the instance's state.
    private static async Task<IResult> StatusAsync(HttpRequest request) =>
        await FindCallerAsync(request) is null ? Unauthorized(request) : NotFound();

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
}

/// <summary>The record and the instance an endpoint call created, and the instance's state.</summary>
internal sealed record EndpointCallResponse(Guid RecordId, Guid InstanceId, string State);
