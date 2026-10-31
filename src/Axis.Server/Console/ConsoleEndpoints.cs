using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Core.Tenancy;
using Axis.Processes.Instances;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Processes;
using Axis.Server.Tenancy;
using Axis.Server.Users;

namespace Axis.Server.Console;

/// <summary>
/// The routes of the operator console under <c>/api/console/</c>. Every route of the group is checked in order: signed-in
/// user (401), operator role named by <c>Console:OperatorRole</c> (403, also for everyone when the setting has no value).
/// Application policies play no part. The group adds no CSRF check of its own, so the CSRF middleware covers every write
/// that joins it.
/// <para>
/// <c>GET /api/console/tenant</c> answers the tenant of the host: its <c>id</c>, its <c>status</c> and, for each active
/// application in name order, its active release with the release <c>number</c> and <c>label</c> from the release history
/// and the time it was activated. The status is always <c>active</c>, because a host serves only active tenants.
/// </para>
/// <para>
/// The dead letter routes under <c>/api/console/dead-letters</c> list, redeliver and discard the dead letters of every
/// application of the tenant, see <see cref="ConsoleDeadLetterEndpoints"/>.
/// </para>
/// <para>
/// <c>GET /api/console/failed-work</c> lists the failed work of the tenant, newest first, in the paging shape of the record
/// API: the instances that are <c>failed</c> and the connector and package calls whose outcome is <c>unknown</c>, across every
/// application. <c>application</c> (an application id) narrows it to one application. An item names the application, the
/// process, the instance and the step, and for a call its connector or package and operation, when it failed and its stored
/// error. It never holds a call input, a history input or output, or a record value.
/// </para>
/// <para>
/// <c>GET /api/console/webhook-deliveries</c> answers one page of the webhook deliveries of the tenant, newest first,
/// filtered by the <c>application</c> and <c>webhook</c> ids. An item names its application, webhook and event from the
/// release the delivery was staged with, or <see langword="null"/> when that release no longer holds the webhook. It
/// gives the state, the attempts, the last status code and when it was delivered. The answer also lists the webhooks of
/// every active release for the view's filter. It never holds a body, a URL, a secret or a binding value.
/// </para>
/// <para>
/// <c>GET /api/console/instances/{id}</c> answers one instance, whatever its application or process, with the shape of the
/// process inspection detail. The operator role stands in for the <c>inspect</c> grant and for a <c>read</c> grant with no
/// <c>fields</c> list, and the instance's subject record need not exist. A call's stored input is hidden by the masking
/// rule of process inspection: when its mapping reads or writes a sensitive field, or a field the active release no longer
/// has. The active release is the pinned release when the application has no active release. A malformed id and an unknown
/// id are one 404. <c>commands</c> is empty and no call is resolvable, because the console has no command routes yet.
/// </para>
/// </summary>
internal static class ConsoleEndpoints
{
    // A host serves only active tenants: a suspended tenant's hosts answer 503, so a request that got here is on an active one.
    private const string ActiveStatus = "active";

    public static void MapConsoleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var console = endpoints.MapGroup("/api/console");
        console.AddEndpointFilter(RequireOperatorAsync);
        console.MapGet("/tenant", GetTenantAsync);
        console.MapGet("/failed-work", ListFailedWorkAsync);
        console.MapGet("/webhook-deliveries", ListWebhookDeliveriesAsync);
        console.MapGet("/instances/{id}", GetInstanceAsync);
        console.MapConsoleDeadLetterEndpoints();
        console.MapConsoleOperatorEndpoints();
    }

    private static async ValueTask<object?> RequireOperatorAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        var settings = httpContext.RequestServices.GetRequiredService<ConsoleSettings>();
        return settings.Allows(user.Roles) ? await next(context) : Grants.Forbidden();
    }

    private static async Task<IResult> GetTenantAsync(
        ITenantContextAccessor tenants,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        if (tenants.Current is not { } tenant)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This host has no tenant.");
        }

        var store = new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken));
        var applications = new List<ConsoleApplication>();
        foreach (var active in await store.ListAsync(cancellationToken))
        {
            // A release can be in the history more than once, always with one number, so the first row of it has it.
            var activation = (await store.ListActivationsAsync(active.ApplicationId, cancellationToken))
                .FirstOrDefault(row => row.ReleaseId == active.ReleaseId);
            applications.Add(new ConsoleApplication(
                active.ApplicationId, active.Name, active.ReleaseId, activation?.Number, activation?.Label, active.ActivatedAt));
        }

        return Results.Ok(new ConsoleTenantResponse(tenant.TenantId, ActiveStatus, applications));
    }

    private static async Task<IResult> ListFailedWorkAsync(
        string? application,
        string? page,
        string? pageSize,
        ActiveApplicationResolver resolver,
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
                errors["application"] = ["Must be the id of an application."];
            }
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var work = await FailedWorkQueries.ListAsync(await database.GetConnectionAsync(cancellationToken), applicationId, pageNumber, size, cancellationToken);
        var activeNames = (await new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken)).ListAsync(cancellationToken))
            .ToDictionary(active => active.ApplicationId, active => active.Name);

        // The process and, for an application that is not active, the application name come from the release the instance is pinned to.
        var models = new Dictionary<Guid, ApplicationModel?>();
        var items = new List<ConsoleFailedWorkItem>();
        foreach (var item in work.Items)
        {
            if (!models.TryGetValue(item.ReleaseId, out var model))
            {
                model = await resolver.FindReleaseModelAsync(item.ApplicationId, item.ReleaseId, cancellationToken);
                models[item.ReleaseId] = model;
            }

            items.Add(new ConsoleFailedWorkItem(
                item.Kind,
                item.ApplicationId,
                activeNames.GetValueOrDefault(item.ApplicationId) ?? model?.Manifest.Name,
                model?.Processes.FirstOrDefault(process => process.Id == item.ProcessId)?.Name,
                item.InstanceId,
                item.Step,
                item.CallId,
                item.Connector,
                item.Package,
                item.Operation,
                item.FailedAt.UtcDateTime,
                item.Error));
        }

        return Results.Ok(new ConsoleFailedWorkResponse(items, pageNumber, size, work.TotalCount));
    }

    private static async Task<IResult> ListWebhookDeliveriesAsync(
        string? application,
        string? webhook,
        string? page,
        string? pageSize,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        PagingQuery.TryRead(page, pageSize, errors, out var pageNumber, out var size);
        var applicationId = ReadId(application, "application", errors);
        var webhookId = ReadId(webhook, "webhook", errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        var result = await WebhookDeliveryList.ListAsync(connection, applicationId, webhookId, pageNumber, size, cancellationToken);

        // A delivery is named by the release it was staged with. That model is cached, so each release is read once.
        var releases = new Dictionary<(Guid Application, Guid Release), ApplicationModel?>();
        var items = new List<ConsoleWebhookDelivery>();
        foreach (var row in result.Items)
        {
            var key = (row.ApplicationId, row.ReleaseId);
            if (!releases.TryGetValue(key, out var model))
            {
                model = await resolver.FindReleaseModelAsync(row.ApplicationId, row.ReleaseId, cancellationToken);
                releases[key] = model;
            }

            var hook = model?.Webhooks.FirstOrDefault(candidate => candidate.Id == row.WebhookId);
            var @event = hook is null ? null : model!.Events.FirstOrDefault(candidate => candidate.Id == hook.Event.Id);
            items.Add(new ConsoleWebhookDelivery(
                row.Id, row.ApplicationId, model?.Manifest.Name, row.WebhookId, hook?.Name, row.EventId, @event?.Name,
                row.State, row.Attempts, row.StatusCode, row.CreatedAt, row.DeliveredAt));
        }

        var webhooks = new List<ConsoleWebhook>();
        foreach (var model in await resolver.ListAsync(cancellationToken))
        {
            webhooks.AddRange(model.Webhooks
                .OrderBy(hook => hook.Name, StringComparer.Ordinal)
                .Select(hook => new ConsoleWebhook(hook.Id, hook.Name, model.Manifest.Id, model.Manifest.Name)));
        }

        return Results.Ok(new ConsoleWebhookDeliveryListResponse(items, pageNumber, size, result.TotalCount, webhooks));
    }

    private static Guid? ReadId(string? value, string name, SortedDictionary<string, string[]> errors)
    {
        if (value is null)
        {
            return null;
        }

        if (Guid.TryParseExact(value, "D", out var id))
        {
            return id;
        }

        errors[name] = ["Must be a hyphenated UUID."];
        return null;
    }

    private static async Task<IResult> GetInstanceAsync(
        string id,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        PackageCatalog packages,
        CancellationToken cancellationToken)
    {
        var notFound = Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No instance exists with this id.");
        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var instanceId))
        {
            return notFound;
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        if (await InstanceQueries.FindByIdAsync(connection, instanceId, cancellationToken) is not { } row
            || await resolver.FindReleaseModelAsync(row.ApplicationId, row.ReleaseId, cancellationToken) is not { } pinned)
        {
            return notFound;
        }

        // The active release gives the fields the operator may read, so an application with no active release uses the pinned one.
        var store = new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken));
        var activeRelease = (await store.ListAsync(cancellationToken)).FirstOrDefault(candidate => candidate.ApplicationId == row.ApplicationId);
        var active = activeRelease is null ? pinned : await resolver.GetReleaseModelAsync(activeRelease.ReleaseId, cancellationToken);
        var process = active.Processes.FirstOrDefault(candidate => candidate.Id == row.ProcessId)
            ?? pinned.Processes.FirstOrDefault(candidate => candidate.Id == row.ProcessId);
        if (process is null)
        {
            return notFound;
        }

        var subject = row.SubjectEntityId is { } subjectEntityId
            ? active.FindEntity(subjectEntityId) ?? pinned.FindEntity(subjectEntityId)
            : null;

        // The commands run against the active release, so they are offered only when it has the process.
        var viewer = activeRelease is not null && active.Processes.Any(candidate => candidate.Id == row.ProcessId)
            ? InspectionViewer.Operator
            : InspectionViewer.OperatorReadOnly;
        return Results.Ok(await ProcessInspectionEndpoints.DetailAsync(
            connection, resolver, packages, active, process, subject, row, viewer, cancellationToken));
    }
}

internal sealed record ConsoleTenantResponse(string Id, string Status, IReadOnlyList<ConsoleApplication> Applications);

internal sealed record ConsoleApplication(Guid Id, string Name, Guid ReleaseId, int? ReleaseNumber, string? ReleaseLabel, DateTimeOffset ActivatedAt);

internal sealed record ConsoleFailedWorkResponse(IReadOnlyList<ConsoleFailedWorkItem> Items, int Page, int PageSize, long TotalCount);

internal sealed record ConsoleFailedWorkItem(
    string Kind,
    Guid ApplicationId,
    string? Application,
    string? Process,
    Guid InstanceId,
    string Step,
    Guid? CallId,
    string? Connector,
    string? Package,
    string? Operation,
    DateTime FailedAt,
    string? Error);

internal sealed record ConsoleWebhookDeliveryListResponse(
    IReadOnlyList<ConsoleWebhookDelivery> Items,
    int Page,
    int PageSize,
    long TotalCount,
    IReadOnlyList<ConsoleWebhook> Webhooks);

internal sealed record ConsoleWebhookDelivery(
    Guid Id,
    Guid ApplicationId,
    string? Application,
    Guid WebhookId,
    string? Webhook,
    Guid EventId,
    string? Event,
    string State,
    int Attempts,
    int? StatusCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt);

internal sealed record ConsoleWebhook(Guid Id, string Name, Guid ApplicationId, string Application);
