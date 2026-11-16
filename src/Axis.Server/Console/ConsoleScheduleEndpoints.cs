using Axis.Configuration.Model;
using Axis.Processes.Instances;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Tenancy;

namespace Axis.Server.Console;

/// <summary>
/// The schedule route of the operator console, in the group of <see cref="ConsoleEndpoints"/>, so the group's checks
/// (signed-in user 401, operator role 403) come first. The list at <c>GET /api/console/schedules</c> answers one page,
/// <c>{ "items": [...], "page", "pageSize", "totalCount" }</c>, of the schedule triggers of the active release of every
/// application. It reads the record API's <c>page</c>, <c>pageSize</c>, <c>sort</c>, <c>search</c> and <c>filter</c> through
/// <see cref="ListQuery{T}"/>, over the declared fields <c>application</c>, <c>trigger</c>, <c>process</c>, <c>schedule</c>,
/// <c>timeZone</c>, <c>lastOccurrenceAt</c> and <c>nextOccurrenceAt</c>, and a search looks in <c>application</c>,
/// <c>trigger</c> and <c>process</c>. An invalid parameter, and a sort or filter on another field, is a 400 that names the
/// parameter. Without <c>sort</c> the items are ordered by application name and then by trigger name, see
/// <c>docs/reference/operator-console.md#api</c>. An optional <c>application</c> (an application id, a hyphenated UUID)
/// keeps one application, and an invalid one is a 400 that names it. A module's schedule trigger is listed once for each
/// application that uses the module, because each application holds its own copy of it. A trigger is listed only when the
/// active release has it and <c>axis.trigger_schedules</c> holds its state, so a trigger that a newer release removed, and
/// every trigger of an application with no active release, is left out. An item names the application, the trigger, its
/// process, its <c>schedule</c> and <c>timeZone</c>, the latest occurrence that ran (<c>lastOccurrenceAt</c>, null while none
/// has) and the next occurrence (<c>nextOccurrenceAt</c>).
/// </summary>
internal static class ConsoleScheduleEndpoints
{
    public static void MapConsoleScheduleEndpoints(this RouteGroupBuilder console) =>
        console.MapGet("/schedules", ListAsync);

    private static readonly IReadOnlyList<ListColumn<ConsoleScheduleItem>> _listColumns =
    [
        new("application", ListColumnKind.Text, item => item.Application, Searchable: true),
        new("trigger", ListColumnKind.Text, item => item.Trigger, Searchable: true),
        new("process", ListColumnKind.Text, item => item.Process, Searchable: true),
        new("schedule", ListColumnKind.Text, item => item.Schedule),
        new("timeZone", ListColumnKind.Text, item => item.TimeZone),
        new(
            "lastOccurrenceAt",
            ListColumnKind.Date,
            item => item.LastOccurrenceAt is { } at ? DateOnly.FromDateTime(at) : (DateOnly?)null,
            Order: item => item.LastOccurrenceAt),
        new("nextOccurrenceAt", ListColumnKind.Date, item => DateOnly.FromDateTime(item.NextOccurrenceAt), Order: item => item.NextOccurrenceAt),
    ];

    private static async Task<IResult> ListAsync(
        string? application,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
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

        var query = ListQuery<ConsoleScheduleItem>.Read(httpContext.Request, _listColumns, errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var models = await resolver.ListAsync(cancellationToken);
        var rows = (await TriggerSchedules.ListAsync(await database.GetConnectionAsync(cancellationToken), applicationId, cancellationToken))
            .ToDictionary(row => row.TriggerId);
        var items = new List<ConsoleScheduleItem>();
        foreach (var model in models.Where(model => applicationId is null || model.Manifest.Id == applicationId))
        {
            foreach (var trigger in model.Triggers
                .Where(trigger => trigger.Type == TriggerType.Schedule)
                .OrderBy(trigger => trigger.Name, StringComparer.Ordinal))
            {
                if (!rows.TryGetValue(trigger.Id, out var row) || row.ApplicationId != model.Manifest.Id)
                {
                    continue;
                }

                items.Add(new ConsoleScheduleItem(
                    model.Manifest.Id,
                    model.Manifest.Name,
                    trigger.Id,
                    trigger.Name,
                    trigger.Process.Name,
                    row.Schedule ?? trigger.Schedule!.Expression,
                    row.TimeZone ?? trigger.TimeZone!.Id,
                    row.LastOccurrenceAt?.UtcDateTime,
                    row.NextOccurrenceAt.UtcDateTime));
            }
        }

        return Results.Ok(ToResponse(query.Apply(items)));
    }

    private static ConsoleScheduleListResponse ToResponse(ListPage<ConsoleScheduleItem> page) =>
        new(page.Items, page.Page, page.PageSize, page.TotalCount);
}

/// <summary>One page of the schedule triggers of the active releases of the tenant, with the number that matched before it was cut.</summary>
internal sealed record ConsoleScheduleListResponse(IReadOnlyList<ConsoleScheduleItem> Items, int Page, int PageSize, long TotalCount);

/// <summary>A schedule trigger of an active release with its latest occurrence that ran, which is null while none has, and its next occurrence.</summary>
internal sealed record ConsoleScheduleItem(
    Guid ApplicationId,
    string Application,
    Guid TriggerId,
    string Trigger,
    string Process,
    string Schedule,
    string TimeZone,
    DateTime? LastOccurrenceAt,
    DateTime NextOccurrenceAt);
