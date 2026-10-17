using Axis.Configuration.Model;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;

namespace Axis.Server.Processes;

/// <summary>
/// The endpoints for a user who may inspect a process. The list at
/// <c>GET /api/apps/{app}/processes/{process}/instances</c> shows the instances of the process,
/// newest first and paged as the record API pages, whatever release each is pinned to. The
/// <c>state</c> parameter keeps one state, and <c>subject</c> keeps the instances of one record. A
/// request is checked in order: session (401), application and process (404), the user's
/// <c>inspect</c> grant on the process (403), then the parameters (400), each keyed by its name.
/// An instance is listed only when its subject record exists and matches one of the user's read
/// filters on the subject entity in the active release. The user's <c>read</c> grant on that
/// entity is needed for it, so without one the list is empty. A hidden instance is in neither the
/// page nor the total. A read filter that fails at run time is a 400. The list writes no audit
/// record. The problem titles never contain text from the request.
/// </summary>
internal static class ProcessInspectionEndpoints
{
    private static readonly string[] _states =
    [
        ProcessStarts.Running,
        ProcessStarts.Waiting,
        ProcessStarts.Completed,
        ProcessStarts.Failed,
        ProcessStarts.Cancelled,
    ];

    public static void MapProcessInspectionEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/apps/{app}/processes/{process}/instances", ListAsync);

    // The query parameters are bound as strings so every invalid one is reported in one problem.
    private static async Task<IResult> ListAsync(
        string app,
        string process,
        string? state,
        string? subject,
        string? page,
        string? pageSize,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
        {
            return NotFound("No application is active under this name.");
        }

        var application = active.Model;
        if (!application.TryGetProcess(process, out var model))
        {
            return NotFound("The application has no process with this name.");
        }

        if (!PolicyEvaluator.AllowsInspect(application, user.Roles, model))
        {
            return Grants.Forbidden();
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        PagingQuery.TryRead(page, pageSize, errors, out var pageNumber, out var size);
        if (state is not null && !_states.Contains(state, StringComparer.Ordinal))
        {
            errors["state"] = [$"Must be one of {string.Join(", ", _states)}."];
        }

        Guid? subjectId = null;
        if (subject is not null)
        {
            if (subject is { Length: 36 } && Guid.TryParseExact(subject, "D", out var parsed))
            {
                subjectId = parsed;
            }
            else
            {
                errors["subject"] = ["Must be the id of a record."];
            }
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (!application.TryGetEntity(model.Entity.Name, out var entity))
        {
            throw new InvalidOperationException($"The subject entity '{model.Entity.Name}' of process '{model.Name}' is not in the model.");
        }

        // An instance whose record the user may not read does not exist for them.
        var read = PolicyEvaluator.Access(application, user.Roles, entity, PolicyAction.Read);
        if (!read.Granted)
        {
            return Results.Ok(new ProcessInstanceListResponse([], pageNumber, size, 0));
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        var condition = read.Filters is { } filters
            ? RecordFilters.Condition(application, entity, RecordCommand.Expressions(filters), user.Id)
            : null;
        if (await InstanceQueries.ListAsync(
                connection, application.Manifest.Id, model.Id, entity, state, subjectId, condition, pageNumber, size, cancellationToken) is not { } result)
        {
            // PostgreSQL cannot fail one branch of an OR, so every filter of the list is named.
            var logger = loggerFactory.CreateLogger(typeof(ProcessInspectionEndpoints).FullName!);
            foreach (var filter in read.Filters!)
            {
                RecordFilterLog.ListFailed(logger, filter.Policy, filter.Grant, entity.Name);
            }

            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "A record filter could not be evaluated for these records.");
        }

        var items = result.Items
            .Select(instance => new ProcessInstanceListItem(
                instance.Id,
                instance.SubjectId,
                instance.State,
                instance.Step,
                instance.ReleaseId,
                instance.CreatedAt.UtcDateTime,
                instance.EndedAt?.UtcDateTime))
            .ToList();
        return Results.Ok(new ProcessInstanceListResponse(items, pageNumber, size, result.TotalCount));
    }

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}

/// <summary>One page of the instances of a process, and the number of them on every page.</summary>
internal sealed record ProcessInstanceListResponse(IReadOnlyList<ProcessInstanceListItem> Items, int Page, int PageSize, long TotalCount);

/// <summary>An instance in the list of a process's instances.</summary>
internal sealed record ProcessInstanceListItem(
    Guid Id,
    Guid SubjectId,
    string State,
    string Step,
    Guid ReleaseId,
    DateTime CreatedAt,
    DateTime? EndedAt);
