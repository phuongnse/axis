using Axis.Core.Tenancy;
using Axis.Data.Profiles;
using Axis.Server.Http;
using Axis.Server.Studio;
using Axis.Server.Tenancy;
using Axis.Tenancy;
using Axis.Tenancy.Realms;

namespace Axis.Server.Users;

/// <summary>
/// The current user, from the session. With nobody signed in it is a 401 problem. It also says whether
/// the user may use Studio (Studio is on and the user holds the author role) and may publish from it (the user also
/// holds the publisher role). It also says whether Studio shows the test controls: the user may use Studio and
/// <c>Studio:Tests</c> is on. For a user of Studio it also gives the host of the tenant's preview tenant, when the
/// server serves one. It also says whether the user may use the operator console: the user holds the role that
/// <c>Console:OperatorRole</c> names. It also says whether the user may administer users: the user holds the role that
/// <c>Users:AdministratorRole</c> names and the tenant is not a preview tenant, which has no users page.
/// <para>
/// <c>GET /api/users?search=&amp;page=&amp;pageSize=</c> lists the users of the tenant's realm to an administrator. It reads
/// them through the realm's users client and joins <c>axis.user_profiles</c> for the status and the last sign-in. The search
/// matches name or email. <c>page</c> starts at 1 and defaults to 1, and <c>pageSize</c> is 1 to 50 and defaults to 50, otherwise
/// the answer is 400. A request is checked in order: signed-in user (401), administrator role (403), a tenant with a users
/// client (404), the paging (400). A failure at the identity provider is a 502 with a fixed title, and the log names the step
/// and the status code, never a body or a secret.
/// </para>
/// </summary>
internal static partial class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me", GetCurrentUserAsync);
        endpoints.MapGet("/api/users", ListUsersAsync);
    }

    private const int DefaultPageSize = 50;

    private static async Task<IResult> ListUsersAsync(
        HttpRequest request,
        string? search,
        int? page,
        int? pageSize,
        UsersSettings users,
        ITenantCatalog catalog,
        ITenantContextAccessor tenants,
        TenantDatabase database,
        IHttpClientFactory httpClients,
        ILogger<UserLog> logger,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        if (!users.Allows(user.Roles))
        {
            return Grants.Forbidden();
        }

        if (IsPreview(catalog, tenants) || tenants.Current is not { } tenant || catalog.Find(tenant.TenantId)?.RealmUsers is not { } access)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This tenant has no users page.");
        }

        var number = page ?? 1;
        var size = pageSize ?? DefaultPageSize;
        if (number < 1 || size < 1 || size > DefaultPageSize)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The page must be 1 or more and the page size from 1 to 50.");
        }

        RealmUserPage listed;
        try
        {
            using var client = httpClients.CreateClient(nameof(UserEndpoints));
            listed = await new RealmUsers(client, access).ListAsync(search, (number - 1) * size, size, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            LogListFailed(logger, tenant.TenantId, exception.Message);
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The identity provider could not list the users.");
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        var signIns = await UserProfiles.FindLastSignInsAsync(connection, [.. listed.Users.Select(realmUser => realmUser.Id)], cancellationToken);
        return Results.Ok(new UserListResponse(
            [.. listed.Users.Select(realmUser => new UserListItem(
                realmUser.Id,
                DisplayName(realmUser),
                realmUser.Email,
                realmUser.ProviderAlias,
                Status(realmUser.Enabled, signIns.ContainsKey(realmUser.Id)),
                signIns.TryGetValue(realmUser.Id, out var signedInAt) ? signedInAt : null))],
            listed.TotalCount));
    }

    /// <summary><c>disabled</c> for a disabled realm user, otherwise <c>invited</c> without a profile and <c>active</c> with one.</summary>
    internal static string Status(bool enabled, bool hasProfile) =>
        !enabled ? "disabled" : hasProfile ? "active" : "invited";

    // The first and last name, or the username when the user has neither.
    private static string DisplayName(RealmUser user) =>
        string.Join(' ', new[] { user.FirstName, user.LastName }.Where(name => name is not null)) is { Length: > 0 } name ? name : user.Username;

    [LoggerMessage(LogLevel.Warning, "The users of tenant {TenantId} could not be listed. {Reason}")]
    private static partial void LogListFailed(ILogger logger, string tenantId, string reason);

    // It takes the request rather than the context, so it binds as a route handler and its result is written.
    private static async Task<IResult> GetCurrentUserAsync(
        HttpRequest request, StudioOptions studio, ConsoleSettings console, UsersSettings users, ITenantCatalog catalog, ITenantContextAccessor tenants) =>
        await CurrentUsers.FindAsync(request.HttpContext) is { } user
            ? Results.Ok(new CurrentUserMeResponse(
                user.Id,
                user.DisplayName,
                user.Roles,
                studio.Allows(user.Roles),
                studio.AllowsPublish(user.Roles),
                studio.AllowsTests(user.Roles),
                studio.AllowsCreate(user.Roles),
                PreviewHost(studio.Allows(user.Roles), catalog, tenants),
                console.Allows(user.Roles),
                users.Allows(user.Roles) && !IsPreview(catalog, tenants)))
            : Grants.SignInRequired();

    private static bool IsPreview(ITenantCatalog catalog, ITenantContextAccessor tenants) =>
        tenants.Current is { } tenant && catalog.Find(tenant.TenantId)?.PreviewOf is not null;

    private static string? PreviewHost(bool studioUser, ITenantCatalog catalog, ITenantContextAccessor tenants) =>
        studioUser && tenants.Current is { } tenant && StudioPreview.Find(catalog, tenant.TenantId)?.Hosts is [var host, ..] ? host : null;
}

/// <summary>The category of the log lines of the user routes.</summary>
internal sealed class UserLog;

/// <summary>The answer of <c>GET /api/users</c>: one page of the realm's users and the number of all users that match the search.</summary>
internal sealed record UserListResponse(IReadOnlyList<UserListItem> Items, int TotalCount);

/// <summary>
/// A realm user of the list. <c>Provider</c> is <c>null</c> for an account, otherwise the alias of the user's federated
/// identity. <c>Status</c> is <c>invited</c>, <c>active</c> or <c>disabled</c>. <c>LastSignedInAt</c> is <c>null</c> without a profile.
/// </summary>
internal sealed record UserListItem(string Id, string DisplayName, string? Email, string? Provider, string Status, DateTimeOffset? LastSignedInAt);

/// <summary>The signed-in user's id, display name and role names.</summary>
internal sealed record CurrentUserResponse(string Id, string DisplayName, IReadOnlyList<string> Roles);

/// <summary>
/// The answer of <c>GET /api/me</c>: the current user, whether they may use Studio, publish from it, see its test controls and create applications in it, and the host of
/// the tenant's preview tenant. <c>StudioPreviewHost</c> is <c>null</c> when the user may not use Studio or the tenant has
/// no served preview tenant. <c>Console</c> is true when the user holds the operator console role.
/// <c>Users</c> is true when the user holds the administrator role and the tenant is not a preview tenant.
/// </summary>
internal sealed record CurrentUserMeResponse(
    string Id, string DisplayName, IReadOnlyList<string> Roles, bool Studio, bool StudioPublisher, bool StudioTest, bool StudioCreate, string? StudioPreviewHost, bool Console, bool Users);
