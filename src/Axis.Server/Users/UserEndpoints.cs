using Axis.Core.Tenancy;
using Axis.Server.Studio;
using Axis.Tenancy;

namespace Axis.Server.Users;

/// <summary>
/// The current user, from the session. With nobody signed in it is a 401 problem. It also says whether
/// the user may use Studio (Studio is on and the user holds the author role) and may publish from it (the user also
/// holds the publisher role). It also says whether Studio shows the test controls: the user may use Studio and
/// <c>Studio:Tests</c> is on. For a user of Studio it also gives the host of the tenant's preview tenant, when the
/// server serves one.
/// </summary>
internal static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me", GetCurrentUserAsync);
    }

    // It takes the request rather than the context, so it binds as a route handler and its result is written.
    private static async Task<IResult> GetCurrentUserAsync(
        HttpRequest request, StudioOptions studio, ITenantCatalog catalog, ITenantContextAccessor tenants) =>
        await CurrentUsers.FindAsync(request.HttpContext) is { } user
            ? Results.Ok(new CurrentUserMeResponse(
                user.Id,
                user.DisplayName,
                user.Roles,
                studio.Allows(user.Roles),
                studio.AllowsPublish(user.Roles),
                studio.AllowsTests(user.Roles),
                PreviewHost(studio.Allows(user.Roles), catalog, tenants)))
            : Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Nobody is signed in.");

    private static string? PreviewHost(bool studioUser, ITenantCatalog catalog, ITenantContextAccessor tenants) =>
        studioUser && tenants.Current is { } tenant && StudioPreview.Find(catalog, tenant.TenantId)?.Hosts is [var host, ..] ? host : null;
}

/// <summary>The signed-in user's id, display name and role names.</summary>
internal sealed record CurrentUserResponse(string Id, string DisplayName, IReadOnlyList<string> Roles);

/// <summary>
/// The answer of <c>GET /api/me</c>: the current user, whether they may use Studio, publish from it and see its test controls, and the host of
/// the tenant's preview tenant. <c>StudioPreviewHost</c> is <c>null</c> when the user may not use Studio or the tenant has
/// no served preview tenant.
/// </summary>
internal sealed record CurrentUserMeResponse(
    string Id, string DisplayName, IReadOnlyList<string> Roles, bool Studio, bool StudioPublisher, bool StudioTest, string? StudioPreviewHost);
