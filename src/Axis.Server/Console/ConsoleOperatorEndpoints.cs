using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Core.Tenancy;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Instances;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Processes;
using Axis.Server.Tenancy;
using Axis.Server.Users;

namespace Axis.Server.Console;

/// <summary>
/// The operator command routes of the operator console, in the group of <see cref="ConsoleEndpoints"/>, so the group's checks
/// (signed-in user 401, operator role 403) come first. A cancel at <c>POST /api/console/instances/{id}/cancel</c>, a retry at
/// <c>POST /api/console/instances/{id}/retry</c> and a resolve at <c>POST /api/console/instances/{id}/calls/{callId}/resolve</c>
/// run the commands of <see cref="ProcessOperatorEndpoints"/> with the same rules, bodies and problem titles, on any instance
/// of the tenant. The operator role stands in for the <c>cancel</c>, <c>retry</c> and <c>resolve</c> grants and for a
/// <c>read</c> grant with no filters on the subject record, so the operator needs no grant in the application and a command
/// runs even when they could not read the record. A request is checked in order after the group: content type (415), the body
/// (400), which is the body of the process route, the <c>id</c> and then the <c>callId</c> (404 when it is not a hyphenated
/// UUID), the instance (404 when none has the id), the active release of the instance's application (409 when it has none)
/// and the instance's process in that release (409 when it is no longer there). The CSRF middleware refuses a write without
/// its header with 403. Then the command runs against the active release, in one transaction and under the start lock as the
/// process route does, with the operator as actor, and a command that is refused writes nothing. When the active release
/// changes while a retry or a resolve waits for the start lock, the command runs again from the release, and answers 503
/// after three attempts. The problem titles never contain text from the request.
/// </summary>
internal static class ConsoleOperatorEndpoints
{
    public static void MapConsoleOperatorEndpoints(this RouteGroupBuilder console)
    {
        console.MapPost("/instances/{id}/cancel", CancelAsync);
        console.MapPost("/instances/{id}/retry", RetryAsync);
        console.MapPost("/instances/{id}/calls/{callId}/resolve", ResolveAsync);
    }

    private static async Task<IResult> CancelAsync(
        string id,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        SensitiveValues sensitive,
        ITenantContextAccessor tenants,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        var (reason, errors) = ProcessOperatorEndpoints.ReadReason(await ProcessOperatorEndpoints.ReadBodyAsync(request, cancellationToken));
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var instanceId))
        {
            return InstanceNotFound();
        }

        var user = await OperatorAsync(request);
        var target = await FindAsync(instanceId, resolver, database, cancellationToken);
        if (target.Failure is not null)
        {
            return target.Failure;
        }

        var (entity, read) = SubjectAccess(target.Active!.Model, target.Process!);
        return await ProcessOperatorEndpoints.RunCancelAsync(
            database,
            target.Active.Model,
            target.Process!,
            entity,
            read,
            instanceId,
            user.Id,
            reason!,
            new RecordCipher(sensitive, (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId),
            loggerFactory,
            cancellationToken);
    }

    private static async Task<IResult> RetryAsync(
        string id,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        SensitiveValues sensitive,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        var (reason, errors) = ProcessOperatorEndpoints.ReadReason(await ProcessOperatorEndpoints.ReadBodyAsync(request, cancellationToken));
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var instanceId))
        {
            return InstanceNotFound();
        }

        var user = await OperatorAsync(request);
        var tenantId = (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;

        // A release change under the start lock sends the request back to resolution, so every check runs on the new release.
        for (var attempt = 0; attempt < ReleaseRetry.MaxAttempts; attempt++)
        {
            var target = await FindAsync(instanceId, resolver, database, cancellationToken);
            if (target.Failure is not null)
            {
                return target.Failure;
            }

            var (entity, read) = SubjectAccess(target.Active!.Model, target.Process!);
            if (await ProcessOperatorEndpoints.RunRetryAsync(
                database,
                tenantId,
                target.Active.Model,
                target.Active.ReleaseId,
                target.Process!,
                entity,
                read,
                instanceId,
                user.Id,
                reason!,
                new RecordCipher(sensitive, tenantId),
                loggerFactory,
                cancellationToken) is { } result)
            {
                return result;
            }
        }

        return ReleaseRetry.Changed();
    }

    private static async Task<IResult> ResolveAsync(
        string id,
        string callId,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        PackageCatalog packages,
        SensitiveValues sensitive,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        var (body, errors) = ProcessOperatorEndpoints.ReadResolve(await ProcessOperatorEndpoints.ReadBodyAsync(request, cancellationToken));
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var instanceId))
        {
            return InstanceNotFound();
        }

        if (callId is not { Length: 36 } || !Guid.TryParseExact(callId, "D", out var operationId))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No call of this instance exists with this id.");
        }

        var user = await OperatorAsync(request);
        var tenantId = (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;

        // A release change under the start lock sends the request back to resolution, so every check runs on the new release.
        for (var attempt = 0; attempt < ReleaseRetry.MaxAttempts; attempt++)
        {
            var target = await FindAsync(instanceId, resolver, database, cancellationToken);
            if (target.Failure is not null)
            {
                return target.Failure;
            }

            var (entity, read) = SubjectAccess(target.Active!.Model, target.Process!);
            if (await ProcessOperatorEndpoints.RunResolveAsync(
                database,
                resolver,
                packages,
                tenantId,
                target.Active,
                target.Process!,
                entity,
                read,
                instanceId,
                operationId,
                user.Id,
                body!,
                new RecordCipher(sensitive, tenantId),
                loggerFactory,
                cancellationToken) is { } result)
            {
                return result;
            }
        }

        return ReleaseRetry.Changed();
    }

    // The group's filter has checked the user.
    private static async Task<CurrentUserResponse> OperatorAsync(HttpRequest request) =>
        await CurrentUsers.FindAsync(request.HttpContext)
        ?? throw new InvalidOperationException("The operator console group admitted a request without a user.");

    // The instance, the active release of its application and its process in that release, or the response that refuses the command.
    private static async Task<Target> FindAsync(
        Guid instanceId,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        var connection = await database.GetConnectionAsync(cancellationToken);
        if (await InstanceQueries.FindByIdAsync(connection, instanceId, cancellationToken) is not { } row)
        {
            return new Target(null, null, InstanceNotFound());
        }

        if (await resolver.ResolveReleaseByIdAsync(row.ApplicationId, cancellationToken) is not { } active)
        {
            return new Target(null, null, Conflict("The application of the instance has no active release."));
        }

        return active.Model.Processes.FirstOrDefault(candidate => candidate.Id == row.ProcessId) is { } process
            ? new Target(active, process, null)
            : new Target(null, null, Conflict("The process of the instance is not in the active release."));
    }

    // The operator stands in for a read grant with no filters, so the record needs no read check. A process without a
    // subject has no record to read, so it has no entity and no read access.
    private static (EntityModel? Entity, RecordAccess? Read) SubjectAccess(ApplicationModel application, ProcessModel process)
    {
        if (process.Entity is not { } subjectEntity)
        {
            return (null, null);
        }

        if (!application.TryGetEntity(subjectEntity.Name, out var entity))
        {
            throw new InvalidOperationException($"The subject entity '{subjectEntity.Name}' of process '{process.Name}' is not in the model.");
        }

        return (entity, new RecordAccess(true, null));
    }

    private static IResult InstanceNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No instance exists with this id.");

    private static IResult Conflict(string title) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: title);

    private sealed record Target(ActiveApplication? Active, ProcessModel? Process, IResult? Failure);
}
