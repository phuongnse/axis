using Axis.Configuration.Releases;
using Axis.Data.Profiles;
using Axis.Data.Records;
using Axis.Server.Http;
using Axis.Server.Studio;
using Axis.Server.Tenancy;
using Npgsql;

namespace Axis.Server.Console;

/// <summary>
/// The Publishing route of the operator console, in the group of <see cref="ConsoleEndpoints"/>, so the group's checks
/// (signed-in user 401, operator role 403) come first and the Studio author and publisher roles play no part.
/// <c>GET /api/console/applications/{id}/publishing</c> answers one application, named by its id (a hyphenated UUID, else a
/// 404), with its active release, its release history and its publish requests. The release history has the rows, order and
/// fields of Studio's releases route, and <c>active</c> is its active release, or null when there is none. The publish
/// requests are one page, which PostgreSQL filters, searches, sorts and pages in the record API's format, over the fields
/// <c>kind</c>, <c>target</c>, <c>status</c>, <c>label</c>, <c>requestedBy</c>, <c>requestedAt</c> and <c>finishedAt</c>.
/// Newest first is the order when there is no sort, and a sorted list breaks its ties newest first. A sort or filter on
/// another field is a 400 that names it. The release history is never paged or filtered. An application
/// that no release or request names answers empty lists. A request shows its kind, target, status, label, note, who asked and
/// when, when it started and finished, and its release. It never holds the file snapshot, the diagnostics or the test results.
/// The route only reads: publishing and activating stay in Studio. The fields and the rules are in
/// docs/reference/operator-console.md#api.
/// </summary>
internal static class ConsolePublishingEndpoints
{
    public static void MapConsolePublishingEndpoints(this RouteGroupBuilder console) =>
        console.MapGet("/applications/{id}/publishing", GetAsync);

    // The columns the list's search, filter and sort can name, over axis.publish_requests, see docs/reference/operator-console.md#api.
    // The requestedAt and finishedAt columns filter by their UTC day and sort by their instant. A search looks in label and requestedBy.
    private static readonly IReadOnlyList<ListColumn<ConsolePublishRequest>> _columns =
    [
        new("kind", ListColumnKind.Enum, row => row.Kind, Values: ["validate", "publish", "activate", "test", "accept"], Sql: "kind"),
        new("target", ListColumnKind.Enum, row => row.Target, Values: ["tenant", "preview"], Sql: "target"),
        new("status", ListColumnKind.Enum, row => row.Status, Values: ["queued", "running", "succeeded", "failed"], Sql: "status"),
        new("label", ListColumnKind.Text, row => row.Label, Searchable: true, Sql: "label"),
        new("requestedBy", ListColumnKind.Text, row => row.RequestedByName ?? row.RequestedBy, Searchable: true, Sql: "coalesce(requested_by_name, requested_by)"),
        new(
            "requestedAt",
            ListColumnKind.Date,
            row => DateOnly.FromDateTime(row.RequestedAt.UtcDateTime),
            Order: row => row.RequestedAt,
            Sql: "(requested_at AT TIME ZONE 'UTC')::date",
            OrderSql: "requested_at"),
        new(
            "finishedAt",
            ListColumnKind.Date,
            row => row.FinishedAt is { } finished ? DateOnly.FromDateTime(finished.UtcDateTime) : null,
            Order: row => row.FinishedAt,
            Sql: "(finished_at AT TIME ZONE 'UTC')::date",
            OrderSql: "finished_at"),
    ];

    private static async Task<IResult> GetAsync(
        string id,
        HttpContext httpContext,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var applicationId))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No application exists with this id.");
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var query = ListQuery<ConsolePublishRequest>.Read(httpContext.Request, _columns, errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var store = new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken));
        var active = await store.FindByApplicationIdAsync(applicationId, cancellationToken);
        var activations = await store.ListActivationsAsync(applicationId, cancellationToken);

        // A release can be in the history more than once, always with one number, so the first row of it has it.
        var activeRelease = active is null
            ? null
            : activations.FirstOrDefault(row => row.ReleaseId == active.ReleaseId) is { } current
                ? new StudioActiveRelease(current.ReleaseId, current.Number, current.ContentHash)
                : null;

        var connection = await database.GetConnectionAsync(cancellationToken);
        var (requests, totalCount) = await ReadRequestsAsync(connection, applicationId, query.Sql(), query.Page, query.PageSize, cancellationToken);
        var names = await UserProfiles.FindDisplayNamesAsync(
            connection,
            [.. activations.Select(row => row.Actor), .. requests.Select(row => row.RequestedBy)],
            cancellationToken);
        var relaxedPoints = await StudioRelaxedWords.ReadAsync(store, [.. activations.Select(row => row.ReleaseId).Distinct()], cancellationToken);
        var releases = activations
            .Select(row =>
            {
                var relaxed = relaxedPoints.GetValueOrDefault(row.ReleaseId) ?? [];
                return new StudioReleaseItem(
                    row.ReleaseId, row.Number, row.ContentHash, row.ActivatedAt, row.Actor, names.GetValueOrDefault(row.Actor) ?? row.ActorName, row.Label, row.Note, row.RequestId,
                    relaxed.Count > 0, relaxed);
            })
            .ToList();
        var items = requests
            .Select(row => row with { RequestedByName = names.GetValueOrDefault(row.RequestedBy) ?? row.RequestedByName })
            .ToList();
        return Results.Ok(new ConsolePublishingResponse(activeRelease, releases, items, query.Page, query.PageSize, totalCount));
    }

    // Each reader is disposed before the next command, because Npgsql allows one open reader per connection.
    private static async Task<(List<ConsolePublishRequest> Items, long TotalCount)> ReadRequestsAsync(
        NpgsqlConnection connection, Guid applicationId, ListSql list, int page, int size, CancellationToken cancellationToken)
    {
        var where = $"WHERE application_id = @application AND {list.Condition}";
        var order = list.Order is null ? "requested_at DESC, id DESC" : $"{list.Order}, requested_at DESC, id DESC";

        long totalCount;
        await using (var count = new NpgsqlCommand($"SELECT count(*) FROM axis.publish_requests {where}", connection))
        {
            count.Parameters.AddWithValue("application", applicationId);
            list.AddTo(count);
            totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

        var items = new List<ConsolePublishRequest>();
        await using var command = new NpgsqlCommand(
            $"""
            SELECT id, kind, target, status, label, note, requested_by, requested_by_name, requested_at, started_at, finished_at, release_id
            FROM axis.publish_requests
            {where}
            ORDER BY {order}
            LIMIT @limit OFFSET @offset
            """,
            connection);
        command.Parameters.AddWithValue("application", applicationId);
        list.AddTo(command);
        command.Parameters.AddWithValue("limit", size);
        command.Parameters.AddWithValue("offset", (long)(page - 1) * size);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ConsolePublishRequest(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                reader.IsDBNull(11) ? null : reader.GetGuid(11)));
        }

        return (items, totalCount);
    }
}

/// <summary>
/// The publishing of one application: its active release, its release history newest first, and one page of its publish
/// requests, newest first until a sort is given, with the number of them on every page.
/// </summary>
internal sealed record ConsolePublishingResponse(
    StudioActiveRelease? Active,
    IReadOnlyList<StudioReleaseItem> Releases,
    IReadOnlyList<ConsolePublishRequest> Requests,
    int Page,
    int PageSize,
    long TotalCount);

/// <summary>
/// A publish request as the console shows it. <c>RequestedByName</c> is the name in the requesting user's profile, else the
/// one stored with the request, else null. It never holds the file snapshot, the diagnostics or the test results.
/// </summary>
internal sealed record ConsolePublishRequest(
    Guid Id,
    string Kind,
    string Target,
    string Status,
    string? Label,
    string? Note,
    string RequestedBy,
    string? RequestedByName,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    Guid? ReleaseId);
