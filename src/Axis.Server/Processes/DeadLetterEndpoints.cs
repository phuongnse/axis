using Axis.Core.Tenancy;
using Axis.Policy;
using Axis.Processes.Instances;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;

namespace Axis.Server.Processes;

/// <summary>
/// The endpoints for the dead letters of a process, see the dead letters of the event delivery reference.
/// The list at <c>GET /api/apps/{app}/processes/{process}/dead-letters</c> shows them newest first and
/// paged as the instance list pages, and an optional <c>state</c> of <c>open</c>, <c>redelivered</c> or
/// <c>discarded</c> keeps one state. It is checked in order: session (401), application and process
/// (404), the user's <c>inspect</c> grant on the process (403), then <c>page</c>, <c>pageSize</c> and
/// <c>state</c> (400). Each item lists the <c>commands</c> the user may take: <c>redeliver</c> and
/// <c>discard</c> for an <c>open</c> dead letter when the user holds the <c>retry</c> grant, none
/// otherwise. An item holds ids, a count and a fixed error text, never a record value.
/// A redeliver at <c>POST …/dead-letters/{id}/redeliver</c> and a discard at <c>POST …/dead-letters/{id}/discard</c>
/// are checked in order: session (401), application and process (404), the user's <c>retry</c> grant on
/// the process (403), content type (415), then the body (400), which is exactly
/// <c>{ "reason": "&lt;text&gt;" }</c> as for the other operator commands. An <c>id</c> that is not a
/// hyphenated UUID, or a dead letter of another process, is a 404. Then one transaction runs
/// <see cref="DeadLetterCommand"/>, which answers 409 for a dead letter that is not <c>open</c>, 409 when the
/// trigger of the dead letter is not in the active release, and 409 for a kind that cannot be redelivered.
/// A command that is refused writes nothing. A success answers 200 with the settled dead letter.
/// The problem titles never contain text from the request.
/// </summary>
internal static class DeadLetterEndpoints
{
    private const string Redeliver = "redeliver";

    private const string Discard = "discard";

    public static void MapDeadLetterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/apps/{app}/processes/{process}/dead-letters", ListAsync);
        endpoints.MapPost("/api/apps/{app}/processes/{process}/dead-letters/{id}/redeliver", RedeliverAsync);
        endpoints.MapPost("/api/apps/{app}/processes/{process}/dead-letters/{id}/discard", DiscardAsync);
    }

    // The query parameters are bound as strings so every invalid one is reported in one problem.
    private static async Task<IResult> ListAsync(
        string app,
        string process,
        string? state,
        string? page,
        string? pageSize,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
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

        if (!PolicyEvaluator.AllowsInspect(application, user.RolesIn(application), model))
        {
            return Grants.Forbidden();
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        PagingQuery.TryRead(page, pageSize, errors, out var pageNumber, out var size);
        if (state is not null && !DeadLetters.States.Contains(state, StringComparer.Ordinal))
        {
            errors["state"] = [$"Must be one of {string.Join(", ", DeadLetters.States)}."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        var result = await DeadLetters.ListAsync(connection, application.Manifest.Id, model.Id, state, pageNumber, size, cancellationToken);
        var mayRetry = PolicyEvaluator.AllowsRetry(application, user.RolesIn(application), model);
        var items = result.Items.Select(row => Item(row, mayRetry)).ToList();
        return Results.Ok(new DeadLetterListResponse(items, pageNumber, size, result.TotalCount));
    }

    private static Task<IResult> RedeliverAsync(
        string app,
        string process,
        string id,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        CancellationToken cancellationToken) =>
        SettleAsync(app, process, id, redeliver: true, request, resolver, database, tenants, cancellationToken);

    private static Task<IResult> DiscardAsync(
        string app,
        string process,
        string id,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        CancellationToken cancellationToken) =>
        SettleAsync(app, process, id, redeliver: false, request, resolver, database, tenants, cancellationToken);

    private static async Task<IResult> SettleAsync(
        string app,
        string process,
        string id,
        bool redeliver,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
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

        if (!PolicyEvaluator.AllowsRetry(application, user.RolesIn(application), model))
        {
            return Grants.Forbidden();
        }

        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        var (reason, errors) = ProcessOperatorEndpoints.ReadReason(await ProcessOperatorEndpoints.ReadBodyAsync(request, cancellationToken));
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var deadLetterId))
        {
            return DeadLetterNotFound();
        }

        var tenantId = (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;
        var connection = await database.GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Nothing starts in this transaction, so the command takes no start lock.
        var result = redeliver
            ? await DeadLetterCommand.RedeliverAsync(
                connection, transaction, tenantId, application, active.ReleaseId, model.Id, deadLetterId, user.Id, reason!, cancellationToken)
            : await DeadLetterCommand.DiscardAsync(
                connection, transaction, tenantId, application, model.Id, deadLetterId, user.Id, reason!, cancellationToken);
        if (result.Outcome != DeadLetterOutcome.Settled)
        {
            await transaction.RollbackAsync(cancellationToken);
            return result.Outcome switch
            {
                DeadLetterOutcome.NotFound => DeadLetterNotFound(),
                DeadLetterOutcome.NotOpen => Conflict("The dead letter is not open."),
                DeadLetterOutcome.TriggerGone => Conflict("The trigger of the dead letter is not in the active release."),
                _ => Conflict("This kind of dead letter cannot be redelivered."),
            };
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Json(Item(result.DeadLetter!, mayRetry: true));
    }

    private static DeadLetterItem Item(DeadLetterRow row, bool mayRetry) =>
        new(
            row.Id,
            row.Kind,
            row.EventId,
            row.TriggerId,
            row.ProcessInstanceId,
            row.EmailId,
            row.WebhookId,
            row.OccurrenceAt?.UtcDateTime,
            row.Attempts,
            row.LastError,
            row.State,
            row.Reason,
            row.SettledBy,
            row.CreatedAt.UtcDateTime,
            row.SettledAt?.UtcDateTime,
            mayRetry && row.State == DeadLetters.Open ? [Redeliver, Discard] : []);

    private static IResult DeadLetterNotFound() => NotFound("No dead letter of this process exists with this id.");

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);

    private static IResult Conflict(string title) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: title);
}

/// <summary>One page of the dead letters of a process, and the number of them on every page.</summary>
internal sealed record DeadLetterListResponse(IReadOnlyList<DeadLetterItem> Items, int Page, int PageSize, long TotalCount);

/// <summary>
/// A dead letter. The ids that its kind lacks are null. <paramref name="Commands"/> lists the operator
/// commands (<c>redeliver</c>, <c>discard</c>) that the user holds the grant for and that its state allows.
/// </summary>
internal sealed record DeadLetterItem(
    Guid Id,
    string Kind,
    Guid? EventId,
    Guid? TriggerId,
    Guid? ProcessInstanceId,
    Guid? EmailId,
    Guid? WebhookId,
    DateTime? OccurrenceAt,
    int Attempts,
    string LastError,
    string State,
    string? Reason,
    string? SettledBy,
    DateTime CreatedAt,
    DateTime? SettledAt,
    IReadOnlyList<string> Commands);
