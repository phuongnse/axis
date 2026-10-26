using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
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
/// A placeholder is filled with its stored value as raw text: a string as it is, a number or boolean as JSON writes it,
/// null as empty text and any other value as JSON. A text key the locale does not have shows as the key itself.
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
            return SignInRequired();
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
        var texts = new Dictionary<Guid, TextResource?>();
        var items = new List<NotificationListItem>(result.Items.Count);
        foreach (var item in result.Items)
        {
            if (!texts.TryGetValue(item.ReleaseId, out var resource))
            {
                var release = await resolver.GetReleaseModelAsync(item.ReleaseId, cancellationToken);
                resource = ChooseTexts(release, locale);
                texts[item.ReleaseId] = resource;
            }

            var values = ReadValues(item.Values);
            items.Add(new NotificationListItem(
                item.Id,
                item.ReleaseId,
                Render(resource, item.SubjectKey, values),
                Render(resource, item.BodyKey, values),
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
            return SignInRequired();
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
            return SignInRequired();
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

    // The profile locale when the release has texts for it, otherwise the first locale in ordinal order.
    private static TextResource? ChooseTexts(ApplicationModel release, string? locale)
    {
        if (locale is not null
            && release.Texts.FirstOrDefault(text => string.Equals(text.Locale, locale, StringComparison.Ordinal)) is { } preferred)
        {
            return preferred;
        }

        return release.Texts.OrderBy(text => text.Locale, StringComparer.Ordinal).FirstOrDefault();
    }

    private static string Render(TextResource? resource, string key, IReadOnlyDictionary<string, string> values) =>
        resource is not null && resource.Texts.TryGetValue(key, out var text) ? TextPlaceholders.Fill(text, values) : key;

    // The values are stored as a JSON object. Each is filled in as raw text.
    private static Dictionary<string, string> ReadValues(string json)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (JsonNode.Parse(json) is JsonObject stored)
        {
            foreach (var (name, value) in stored)
            {
                values[name] = value switch
                {
                    null => string.Empty,
                    JsonValue text when text.TryGetValue<string>(out var raw) => raw,
                    _ => value.ToJsonString(),
                };
            }
        }

        return values;
    }

    private static JsonElement ParseParameters(string? json)
    {
        using var document = JsonDocument.Parse(json ?? "{}");
        return document.RootElement.Clone();
    }

    private static IResult SignInRequired() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign in to read notifications.");

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
