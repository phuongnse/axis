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
using Axis.Studio;

namespace Axis.Server.Console;

/// <summary>
/// The routes of the operator console under <c>/api/console/</c>. Every route of the group is checked in order: signed-in
/// user (401), operator role named by <c>Console:OperatorRole</c> (403, also for everyone when the setting has no value).
/// Application policies play no part. The group adds no CSRF check of its own, so the CSRF middleware covers every write
/// that joins it.
/// <para>
/// <c>GET /api/console/tenant</c> answers the tenant of the host: its <c>id</c>, its <c>status</c> and, for each active
/// application in name order, its active release with the release <c>number</c> and <c>label</c> from the release history
/// and the time it was activated. The status is <c>active</c> or <c>readOnly</c>, because a host serves only those tenants.
/// </para>
/// <para>
/// The dead letter routes under <c>/api/console/dead-letters</c> list, redeliver and discard the dead letters of every
/// application of the tenant, see <see cref="ConsoleDeadLetterEndpoints"/>.
/// </para>
/// <para>
/// <c>GET /api/console/failed-work</c> lists the failed work of the tenant: the instances that are <c>failed</c> and the
/// connector and package calls whose outcome is <c>unknown</c>, across every application. It takes the record API's
/// <c>page</c>, <c>pageSize</c>, <c>sort</c>, <c>search</c> and <c>filter</c> over the fields <c>application</c>,
/// <c>process</c>, <c>instance</c>, <c>stepOrCall</c>, <c>failedAt</c> and <c>error</c>, which PostgreSQL applies, see
/// <see cref="ListQuery{T}"/>. A search looks in <c>stepOrCall</c> and <c>error</c>, and a sort or filter on another field is
/// a 400. The <c>failedAt</c> field filters by its UTC day and sorts by its instant. Without a sort the list is newest
/// first, and a sorted list breaks ties newest first. <c>application</c> (an application id) narrows it to one application.
/// An item names the application, the process, the instance and the step, and for a call its connector or package and
/// operation, when it failed and its stored error. It never holds a call input, a history input or output, or a record
/// value. The answer also lists the processes of every active release with their application.
/// </para>
/// <para>
/// <c>GET /api/console/webhook-deliveries</c> answers one page of the webhook deliveries of the tenant. It takes the record
/// API's <c>page</c>, <c>pageSize</c>, <c>sort</c>, <c>search</c> and <c>filter</c> over the fields <c>application</c>,
/// <c>webhook</c>, <c>event</c>, <c>state</c>, <c>attempts</c>, <c>statusCode</c> and <c>deliveredAt</c>, which PostgreSQL
/// applies, see <see cref="ListQuery{T}"/>. A search looks in the event occurrence id, and a sort or filter on another field
/// is a 400. Without a sort the deliveries are newest first, and a sorted list breaks ties newest first. The optional
/// <c>application</c> and <c>webhook</c> ids keep one each. An item names its application, webhook and event from the
/// release the delivery was staged with, or <see langword="null"/> when that release no longer holds the webhook. It
/// gives the state, the attempts, the last status code and when it was delivered. The answer also lists the webhooks of
/// every active release. It never holds a body, a URL, a secret or a binding value.
/// </para>
/// <para>
/// <c>GET /api/console/schedules</c> lists one page of the schedule triggers of every active release with their last and
/// next occurrence, see <see cref="ConsoleScheduleEndpoints"/>.
/// </para>
/// <para>
/// <c>GET /api/console/instances/{id}</c> answers one instance, whatever its application or process, with the shape of the
/// process inspection detail. The operator role stands in for the <c>inspect</c> grant and for a <c>read</c> grant with no
/// <c>fields</c> list, and the instance's subject record need not exist. A call's stored input is hidden by the masking
/// rule of process inspection: when its mapping reads or writes a sensitive field, or a field the active release no longer
/// has. The active release is the pinned release when the application has no active release. A malformed id and an unknown
/// id are one 404. <c>commands</c> lists the commands that apply, and a call is resolvable, only when the application has an
/// active release that still has the instance's process, because the console's command routes run against that release, see
/// <see cref="ConsoleOperatorEndpoints"/>.
/// </para>
/// <para>
/// <c>GET /api/console/applications/{id}/publishing</c> answers one application's release history and publish requests, read
/// only, see <see cref="ConsolePublishingEndpoints"/>.
/// </para>
/// </summary>
internal static class ConsoleEndpoints
{
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
        console.MapConsolePublishingEndpoints();
        console.MapConsoleScheduleEndpoints();
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

        return Results.Ok(new ConsoleTenantResponse(tenant.TenantId, tenant.ReadOnly ? "readOnly" : "active", applications));
    }

    private static async Task<IResult> ListFailedWorkAsync(
        string? application,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var query = ListQuery<FailedWorkItem>.Read(httpContext.Request, _failedWorkColumns, errors);
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

        var work = await FailedWorkQueries.ListAsync(await database.GetConnectionAsync(cancellationToken), applicationId, query.Sql(), query.Page, query.PageSize, cancellationToken);
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

        var processes = new List<ConsoleProcess>();
        foreach (var model in await resolver.ListAsync(cancellationToken))
        {
            processes.AddRange(model.Processes
                .OrderBy(process => process.Name, StringComparer.Ordinal)
                .Select(process => new ConsoleProcess(process.Id, process.Name, model.Manifest.Id, model.Manifest.Name)));
        }

        return Results.Ok(new ConsoleFailedWorkResponse(items, query.Page, query.PageSize, work.TotalCount, processes));
    }

    // The columns the failed work list's search, filter and sort can name, see docs/reference/operator-console.md#api. The
    // stepOrCall column is the text the table shows, and the failedAt column filters by its UTC day and sorts by its instant.
    private static readonly IReadOnlyList<ListColumn<FailedWorkItem>> _failedWorkColumns =
    [
        new("application", ListColumnKind.Text, row => row.ApplicationId.ToString("D"), Sql: "application_id::text"),
        new("process", ListColumnKind.Text, row => row.ProcessId.ToString("D"), Sql: "process_id::text"),
        new("instance", ListColumnKind.Text, row => row.InstanceId.ToString("D"), Sql: "instance_id::text"),
        new(
            "stepOrCall",
            ListColumnKind.Text,
            row => row.Step,
            Searchable: true,
            Sql: "CASE WHEN kind = 'instance' THEN step ELSE step || ': ' || COALESCE(connector, package) || ' ' || operation END"),
        new(
            "failedAt",
            ListColumnKind.Date,
            row => DateOnly.FromDateTime(row.FailedAt.UtcDateTime),
            Order: row => row.FailedAt,
            Sql: "(failed_at AT TIME ZONE 'UTC')::date",
            OrderSql: "failed_at"),
        new("error", ListColumnKind.Text, row => row.Error, Searchable: true, Sql: "error"),
    ];

    // The columns the list's search, filter and sort can name, over the webhook deliveries, see docs/reference/operator-console.md#api.
    // The deliveredAt column filters by its UTC day and sorts by its instant.
    private static readonly IReadOnlyList<ListColumn<WebhookDeliveryListRow>> _deliveryColumns =
    [
        new("application", ListColumnKind.Text, row => row.ApplicationId.ToString("D"), Sql: "application_id::text"),
        new("webhook", ListColumnKind.Text, row => row.WebhookId.ToString("D"), Sql: "webhook_id::text"),
        new("event", ListColumnKind.Text, row => row.EventId.ToString("D"), Searchable: true, Sql: "event_id::text"),
        new("state", ListColumnKind.Enum, row => row.State, Values: WebhookDeliveryList.States, Sql: "state"),
        new("attempts", ListColumnKind.Number, row => row.Attempts, Sql: "attempts"),
        new("statusCode", ListColumnKind.Number, row => row.StatusCode, Sql: "status_code"),
        new(
            "deliveredAt",
            ListColumnKind.Date,
            row => row.DeliveredAt is { } at ? DateOnly.FromDateTime(at.UtcDateTime) : null,
            Order: row => row.DeliveredAt,
            Sql: "(delivered_at AT TIME ZONE 'UTC')::date",
            OrderSql: "delivered_at"),
    ];

    private static async Task<IResult> ListWebhookDeliveriesAsync(
        string? application,
        string? webhook,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var query = ListQuery<WebhookDeliveryListRow>.Read(httpContext.Request, _deliveryColumns, errors);
        var applicationId = ReadId(application, "application", errors);
        var webhookId = ReadId(webhook, "webhook", errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        var result = await WebhookDeliveryList.ListAsync(
            connection, applicationId, webhookId, query.Sql(), query.Page, query.PageSize, cancellationToken);

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

        return Results.Ok(new ConsoleWebhookDeliveryListResponse(items, query.Page, query.PageSize, result.TotalCount, webhooks));
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
        HttpContext httpContext,
        StudioOptions studio,
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

        // The operator sees the studio address only when they also hold the author role.
        var author = await CurrentUsers.FindAsync(httpContext) is { } user && studio.Allows(user.Roles);
        return Results.Ok(await ProcessInspectionEndpoints.DetailAsync(
            connection, resolver, packages, active, process, subject, row, viewer, author, cancellationToken));
    }
}

internal sealed record ConsoleTenantResponse(string Id, string Status, IReadOnlyList<ConsoleApplication> Applications);

internal sealed record ConsoleApplication(Guid Id, string Name, Guid ReleaseId, int? ReleaseNumber, string? ReleaseLabel, DateTimeOffset ActivatedAt);

internal sealed record ConsoleFailedWorkResponse(IReadOnlyList<ConsoleFailedWorkItem> Items, int Page, int PageSize, long TotalCount, IReadOnlyList<ConsoleProcess> Processes);

internal sealed record ConsoleProcess(Guid Id, string Name, Guid ApplicationId, string Application);

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
