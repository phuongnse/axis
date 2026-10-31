using Axis.Core.Tenancy;
using Axis.Processes.Instances;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Processes;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Npgsql;

namespace Axis.Server.Console;

/// <summary>
/// The dead letter routes of the operator console, in the group of <see cref="ConsoleEndpoints"/>, so the group's checks
/// (signed-in user 401, operator role 403) come first. The list at <c>GET /api/console/dead-letters</c> shows the dead
/// letters of every application of the tenant, newest first and paged as the instance list pages, including the ones that
/// belong to no process. An optional <c>application</c> (an application id, a hyphenated UUID) keeps one application and an
/// optional <c>state</c> of <c>open</c>, <c>redelivered</c> or <c>discarded</c> keeps one state. An invalid <c>application</c>,
/// <c>state</c>, <c>page</c> or <c>pageSize</c> is a 400 that names each. Each item is a dead letter of the process route with
/// its <c>applicationId</c> and <c>processId</c>, and lists <c>redeliver</c> and <c>discard</c> as <c>commands</c> while it is
/// <c>open</c>, because the operator role stands in for the <c>retry</c> grant.
/// A redeliver at <c>POST /api/console/dead-letters/{id}/redeliver</c> and a discard at
/// <c>POST /api/console/dead-letters/{id}/discard</c> are checked in order after the group: content type (415), the body
/// (400), which is exactly <c>{ "reason": "&lt;text&gt;" }</c>, then the <c>id</c> (404 when it is not a hyphenated UUID or
/// no dead letter has it) and the active release of the application of the dead letter (409 when it has none). The CSRF
/// middleware refuses a write without its header with 403. Then one transaction runs <see cref="DeadLetterCommand"/> with no
/// process, which writes the same changes and audit records as the process routes, with the operator as actor, and answers
/// 409 as they do. A command that is refused writes nothing. A success answers 200 with the settled dead letter.
/// The problem titles never contain text from the request.
/// </summary>
internal static class ConsoleDeadLetterEndpoints
{
    private const string Redeliver = "redeliver";

    private const string Discard = "discard";

    public static void MapConsoleDeadLetterEndpoints(this RouteGroupBuilder console)
    {
        console.MapGet("/dead-letters", ListAsync);
        console.MapPost("/dead-letters/{id}/redeliver", RedeliverAsync);
        console.MapPost("/dead-letters/{id}/discard", DiscardAsync);
    }

    // The query parameters are bound as strings so every invalid one is reported in one problem.
    private static async Task<IResult> ListAsync(
        string? application,
        string? state,
        string? page,
        string? pageSize,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        PagingQuery.TryRead(page, pageSize, errors, out var pageNumber, out var size);
        Guid? applicationId = null;
        if (application is not null)
        {
            if (application is { Length: 36 } && Guid.TryParseExact(application, "D", out var parsed))
            {
                applicationId = parsed;
            }
            else
            {
                errors["application"] = ["Must be an application id."];
            }
        }

        if (state is not null && !DeadLetters.States.Contains(state, StringComparer.Ordinal))
        {
            errors["state"] = [$"Must be one of {string.Join(", ", DeadLetters.States)}."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        var result = await DeadLetters.ListTenantAsync(connection, applicationId, state, pageNumber, size, cancellationToken);
        var items = result.Items.Select(Item).ToList();
        return Results.Ok(new ConsoleDeadLetterListResponse(items, pageNumber, size, result.TotalCount));
    }

    private static Task<IResult> RedeliverAsync(
        string id,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        CancellationToken cancellationToken) =>
        SettleAsync(id, redeliver: true, request, resolver, database, tenants, cancellationToken);

    private static Task<IResult> DiscardAsync(
        string id,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        CancellationToken cancellationToken) =>
        SettleAsync(id, redeliver: false, request, resolver, database, tenants, cancellationToken);

    private static async Task<IResult> SettleAsync(
        string id,
        bool redeliver,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
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

        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var deadLetterId))
        {
            return DeadLetterNotFound();
        }

        // The group's filter has checked the user.
        var user = await CurrentUsers.FindAsync(request.HttpContext)
            ?? throw new InvalidOperationException("The operator console group admitted a request without a user.");
        var tenantId = (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;
        var connection = await database.GetConnectionAsync(cancellationToken);

        Guid applicationId;
        await using (var find = new NpgsqlCommand("SELECT application_id FROM axis.dead_letters WHERE id = @id", connection))
        {
            find.Parameters.AddWithValue("id", deadLetterId);
            if (await find.ExecuteScalarAsync(cancellationToken) is not Guid found)
            {
                return DeadLetterNotFound();
            }

            applicationId = found;
        }

        if (await resolver.ResolveReleaseByIdAsync(applicationId, cancellationToken) is not { } active)
        {
            return Conflict("The application of the dead letter has no active release.");
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Nothing starts in this transaction, so the command takes no start lock. No process matches any dead letter of the application.
        var result = redeliver
            ? await DeadLetterCommand.RedeliverAsync(
                connection, transaction, tenantId, active.Model, active.ReleaseId, processId: null, deadLetterId, user.Id, reason!, cancellationToken)
            : await DeadLetterCommand.DiscardAsync(
                connection, transaction, tenantId, active.Model, processId: null, deadLetterId, user.Id, reason!, cancellationToken);
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
        return Results.Json(Item(result.DeadLetter!));
    }

    private static ConsoleDeadLetterItem Item(DeadLetterRow row) =>
        new(
            row.Id,
            row.Kind,
            row.ApplicationId,
            row.ProcessId,
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
            row.State == DeadLetters.Open ? [Redeliver, Discard] : []);

    private static IResult DeadLetterNotFound() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No dead letter exists with this id.");

    private static IResult Conflict(string title) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: title);
}

/// <summary>One page of the dead letters of the tenant, and the number of them on every page.</summary>
internal sealed record ConsoleDeadLetterListResponse(IReadOnlyList<ConsoleDeadLetterItem> Items, int Page, int PageSize, long TotalCount);

/// <summary>
/// A dead letter of the tenant: a <see cref="DeadLetterItem"/> with the application it belongs to and its process, which is
/// null for a webhook delivery of an occurrence that no instance raised.
/// </summary>
internal sealed record ConsoleDeadLetterItem(
    Guid Id,
    string Kind,
    Guid ApplicationId,
    Guid? ProcessId,
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
