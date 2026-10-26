using Axis.Data.Profiles;
using Axis.Policy;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Tenancy;

namespace Axis.Server.Users;

/// <summary>
/// <c>GET /api/apps/{app}/users?search=</c>, the user picker. It lists up to 20 user profiles whose display name
/// contains the search text, ignoring letter case, sorted by display name and then id. A missing or empty search lists the
/// first 20. Each item has only the user's <c>id</c> and <c>displayName</c>. It is the only route that lists user
/// profiles, and it is for choosing who receives a task, so it needs <c>reassign</c> on some process of the active
/// release. A request is checked in order: signed-in user (401), application (404), the grant (403).
/// </summary>
internal static class UserPickerEndpoints
{
    private const int Limit = 20;

    public static void MapUserPickerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/apps/{app}/users", ListAsync);
    }

    private static async Task<IResult> ListAsync(
        string app,
        string? search,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign in to work on tasks.");
        }

        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No application is active under this name.");
        }

        if (!PolicyEvaluator.AllowsReassignAny(active.Model, user.Roles))
        {
            return Grants.Forbidden();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        var profiles = await UserProfiles.SearchAsync(connection, search ?? "", Limit, cancellationToken);
        return Results.Ok(new UserPickerResponse([.. profiles.Select(profile => new UserPickerItem(profile.Id, profile.DisplayName))]));
    }
}

/// <summary>The profiles a user picker lists.</summary>
internal sealed record UserPickerResponse(IReadOnlyList<UserPickerItem> Items);

/// <summary>A user the picker offers: the user id and the display name.</summary>
internal sealed record UserPickerItem(string Id, string DisplayName);
