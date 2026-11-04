using Axis.Configuration.Compilation;
using Axis.Data.Profiles;
using Axis.Presentation.Texts;
using Axis.Processes.Instances;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;

namespace Axis.Server.Processes;

/// <summary>
/// The platform endpoints that list the tenant notices for the current user and mark them read. A tenant notice is a notification
/// that belongs to no application. The current user is the session user. A user sees a notice addressed to the administrators only
/// while they hold the role that <c>Users:AdministratorRole</c> names. Anyone else gets an empty list with zero counts, and marking
/// a notice they do not see is a 404. The answers have the shape of the application notification routes, with <c>releaseId</c> and
/// <c>open</c> always null. A request is checked in order: signed-in user (401), then for a list the paging and <c>unread</c> (400,
/// every bad parameter in one problem), and for a read the notice id and whether the user sees the notice (404).
/// A list is newest first. Its subject and body come from the platform texts in the locale of the user's profile, otherwise from
/// the English texts. A locale matches by its whole tag, ignoring letter case. A placeholder is filled with its stored value
/// formatted by <see cref="NotificationValues"/>: numbers in the conventions of the user's profile locale, or of the chosen texts
/// when the profile has none, and a date-time in UTC, because a notice has no release time zone. A text key the locale does not
/// have shows as the key itself.
/// The read routes write a read row for the session user only, and answer 204 with no body, also for a notice that is already read.
/// Read-all marks the notices the user sees now. The routes are served on the tenant's platform host only. The problem titles
/// never contain text from the request.
/// </summary>
internal static class TenantNoticeEndpoints
{
    private const string FallbackLocale = "en";

    public static void MapTenantNoticeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var notices = endpoints.MapGroup("/api/notifications");
        notices.MapGet("", ListAsync);
        notices.MapPost("/read-all", ReadAllAsync);
        notices.MapPost("/{id}/read", ReadAsync);
    }

    // The query parameters are bound as strings so every invalid one is reported in one problem.
    private static async Task<IResult> ListAsync(
        string? page,
        string? pageSize,
        string? unread,
        HttpContext httpContext,
        UsersSettings users,
        ITextResourceProvider platformTexts,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired();
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

        var connection = await database.GetConnectionAsync(cancellationToken);
        var result = await NotificationQueries.ListTenantNoticesAsync(
            connection, user.Id, PlatformRoleKinds(users, user), unreadOnly, pageNumber, size, cancellationToken);

        var locale = result.Items.Count == 0 ? null : await UserProfiles.FindLocaleAsync(connection, user.Id, cancellationToken);
        var texts = (locale is null ? null : platformTexts.GetTexts(locale)) ?? platformTexts.GetTexts(FallbackLocale);
        var items = new List<NotificationListItem>(result.Items.Count);
        foreach (var item in result.Items)
        {
            var values = NotificationValues.Format(item.Values, item.ValueTypes, locale ?? texts?.Locale, TimeZoneInfo.Utc);
            items.Add(new NotificationListItem(
                item.Id,
                null,
                Render(texts, item.SubjectKey, values),
                Render(texts, item.BodyKey, values),
                null,
                item.Read,
                item.CreatedAt.UtcDateTime));
        }

        return Results.Ok(new NotificationListResponse(items, pageNumber, size, result.TotalCount, result.UnreadCount));
    }

    private static async Task<IResult> ReadAsync(
        string id,
        HttpContext httpContext,
        UsersSettings users,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        if (!NotificationEndpoints.TryParseId(id, out var noticeId))
        {
            return NotificationEndpoints.NotificationNotFound();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        return await NotificationQueries.MarkTenantNoticeReadAsync(
            connection, user.Id, PlatformRoleKinds(users, user), noticeId, cancellationToken)
            ? Results.NoContent()
            : NotificationEndpoints.NotificationNotFound();
    }

    private static async Task<IResult> ReadAllAsync(
        HttpContext httpContext,
        UsersSettings users,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        await NotificationQueries.MarkAllTenantNoticesReadAsync(connection, user.Id, PlatformRoleKinds(users, user), cancellationToken);
        return Results.NoContent();
    }

    // The platform role kinds whose notices the user sees. Only the administrators have notices today.
    private static string[] PlatformRoleKinds(UsersSettings users, CurrentUserResponse user) =>
        users.Allows(user.Roles) ? [PlatformRoles.AdministratorKind] : [];

    private static string Render(TextResources? texts, string key, IReadOnlyDictionary<string, string> values) =>
        texts is not null && texts.Texts.TryGetValue(key, out var text) ? TextPlaceholders.Fill(text, values) : key;
}
