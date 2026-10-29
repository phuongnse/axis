using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Data.Profiles;
using Axis.Policy;
using Axis.Processes.Instances;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;

namespace Axis.Server.Processes;

/// <summary>
/// The notification API endpoints that list the current user's in-app notifications and mark them read. The current
/// user is the session user. A user sees the notifications addressed to their id, and those addressed to a role they
/// hold that the active release declares, so a user whose role the active release drops no longer sees the
/// notifications of that role. A notification addressed both ways is listed once. A request is checked in order:
/// signed-in user (401), application (404), then for a list the paging and <c>unread</c> (400, every bad parameter in
/// one problem), and for a read the notification id and whether the user sees the notification (404).
/// A list is newest first. Its subject and body come from the texts of the release each notification was stored with,
/// in the locale of the user's profile when that release has texts for it, otherwise its first locale in ordinal order.
/// A placeholder is filled with its stored value formatted by <see cref="NotificationValues"/>: numbers in the conventions of
/// the user's profile locale, or of the chosen texts when the profile has none, a date-time in the time zone of the release the
/// notification was stored with, a date as its local date, and enum, text and other values as written.
/// A text key the locale does not have shows as the key itself.
/// The read routes write a read row for the session user only, never for another user, and answer 204 with no body,
/// also for a notification that is already read. Read-all marks the notifications the user sees now.
/// The problem titles never contain text from the request.
/// </summary>
internal static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/api/apps/{app}/notifications");
        notifications.MapGet("", ListAsync);
        notifications.MapPost("/read-all", ReadAllAsync);
        notifications.MapPost("/{id}/read", ReadAsync);
    }

    // The query parameters are bound as strings so every invalid one is reported in one problem.
    private static async Task<IResult> ListAsync(
        string app,
        string? page,
        string? pageSize,
        string? unread,
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
            return ApplicationNotFound();
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var paging = PagingQuery.TryRead(page, pageSize, errors, out var pageNumber, out var size);
        var unreadOnly = false;
        if (unread is not null && !bool.TryParse(unread, out unreadOnly))
        {
            errors["unread"] = ["Must be true or false."];
        }

        if (!paging || errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var roles = PolicyEvaluator.HeldRoles(active.Model, user.Roles);
        var connection = await database.GetConnectionAsync(cancellationToken);
        var result = await NotificationQueries.ListAsync(
            connection, active.Model.Manifest.Id, user.Id, roles, unreadOnly, pageNumber, size, cancellationToken);

        var locale = result.Items.Count == 0 ? null : await UserProfiles.FindLocaleAsync(connection, user.Id, cancellationToken);
        var releases = new Dictionary<Guid, (ApplicationModel Release, TextResource? Texts)>();
        var items = new List<NotificationListItem>(result.Items.Count);
        foreach (var item in result.Items)
        {
            if (!releases.TryGetValue(item.ReleaseId, out var stored))
            {
                var release = await resolver.GetReleaseModelAsync(item.ReleaseId, cancellationToken);
                stored = (release, NotificationTexts.ChooseTexts(release, locale));
                releases[item.ReleaseId] = stored;
            }

            var (storedRelease, resource) = stored;
            var values = NotificationValues.Format(item.Values, item.ValueTypes, locale ?? resource?.Locale, storedRelease.TimeZone);
            items.Add(new NotificationListItem(
                item.Id,
                item.ReleaseId,
                NotificationTexts.Render(resource, item.SubjectKey, values),
                NotificationTexts.Render(resource, item.BodyKey, values),
                item.OpenPage is { } openPage
                    ? new NotificationOpen(openPage, ParseParameters(item.OpenParameters))
                    : null,
                item.Read,
                item.CreatedAt.UtcDateTime));
        }

        return Results.Ok(new NotificationListResponse(items, pageNumber, size, result.TotalCount, result.UnreadCount));
    }

    private static async Task<IResult> ReadAsync(
        string app,
        string id,
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
            return ApplicationNotFound();
        }

        if (!TryParseId(id, out var notificationId))
        {
            return NotificationNotFound();
        }

        var roles = PolicyEvaluator.HeldRoles(active.Model, user.Roles);
        var connection = await database.GetConnectionAsync(cancellationToken);
        return await NotificationQueries.MarkReadAsync(
            connection, active.Model.Manifest.Id, user.Id, roles, notificationId, cancellationToken)
            ? Results.NoContent()
            : NotificationNotFound();
    }

    private static async Task<IResult> ReadAllAsync(
        string app,
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
            return ApplicationNotFound();
        }

        var roles = PolicyEvaluator.HeldRoles(active.Model, user.Roles);
        var connection = await database.GetConnectionAsync(cancellationToken);
        await NotificationQueries.MarkAllReadAsync(connection, active.Model.Manifest.Id, user.Id, roles, cancellationToken);
        return Results.NoContent();
    }

    private static JsonElement ParseParameters(string? json)
    {
        using var document = JsonDocument.Parse(json ?? "{}");
        return document.RootElement.Clone();
    }

    private static IResult ApplicationNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No application is active under this name.");

    private static IResult NotificationNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No notification exists with this id.");

    private static bool TryParseId(string id, out Guid notificationId) =>
        Guid.TryParseExact(id, "D", out notificationId) && id.Length == 36;
}

/// <summary>One page of the notifications the user sees, with the number of them on every page and the number unread.</summary>
internal sealed record NotificationListResponse(IReadOnlyList<NotificationListItem> Items, int Page, int PageSize, long TotalCount, long UnreadCount);

/// <summary>A notification as a list shows it, with the texts rendered from <see cref="ReleaseId"/>. <see cref="Open"/> is null when the notification opens no page.</summary>
internal sealed record NotificationListItem(
    Guid Id,
    Guid ReleaseId,
    string Subject,
    string Body,
    NotificationOpen? Open,
    bool Read,
    DateTime CreatedAt);

/// <summary>The page a notification opens, by its declared name, with the raw page parameters.</summary>
internal sealed record NotificationOpen(string Page, JsonElement Parameters);
