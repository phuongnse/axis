using Axis.Data.Profiles;
using Axis.Processes.Records;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Records;
using Axis.Server.Tenancy;

namespace Axis.Server.Users;

/// <summary>
/// <c>GET /api/apps/{app}/entities/{entity}/fields/{field}/users?search=&amp;id=</c>, and
/// <c>.../entities/{module}/{entity}/fields/{field}/users</c> for an entity of a module. It serves the picker of a text
/// field marked <c>userId</c>. It lists up to 20 user profiles of the tenant whose display name or email contains the
/// search text, ignoring letter case, sorted by display name and then id. A missing or empty search lists the first 20.
/// With <c>id</c> it ignores the search and answers the one profile of that user id, or an empty list when no profile
/// has it, so that a form can show a stored id of someone who has never signed in. Each item has only the user's
/// <c>id</c> and <c>displayName</c>, never the email. Only signed-in users may call it: a service account may not.
/// A request is checked in order: signed-in user (401), then the path, where an unknown entity, a child entity and a
/// field that is no user id field are 404, then the grant (403). The grant is a <c>create</c> or <c>update</c> grant
/// on the entity whose <c>fields</c> cover the field, so only people who may write the field can list user names.
/// </summary>
internal static class UserIdFieldEndpoints
{
    private const int Limit = 20;

    public static void MapUserIdFieldEndpoints(this IEndpointRouteBuilder endpoints)
    {
        foreach (var pattern in new[]
        {
            "/api/apps/{app}/entities/{entity}/fields/{field}/users",
            "/api/apps/{app}/entities/{module}/{entity}/fields/{field}/users",
        })
        {
            endpoints.MapGet(pattern, ListAsync);
        }
    }

    private static async Task<IResult> ListAsync(
        string app,
        string entity,
        string field,
        string? search,
        string? id,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired(httpContext);
        }

        var (application, model, notFound, _) = await RecordEndpoints.ResolveEntityAsync(
            app, RecordEndpoints.EntityName(httpContext, entity), resolver, cancellationToken);
        if (notFound is not null)
        {
            return notFound;
        }

        if (!model!.TryGetField(field, out var found) || !found.UserId)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The entity has no user id field with this name.");
        }

        if (!RecordFieldAccess.CanWrite(application!, user.RolesIn(application!), model, found.Name))
        {
            return Grants.Forbidden();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        if (!string.IsNullOrEmpty(id))
        {
            var names = await UserProfiles.FindDisplayNamesAsync(connection, [id], cancellationToken);
            return Results.Ok(new UserPickerResponse([.. names.Select(name => new UserPickerItem(name.Key, name.Value))]));
        }

        var profiles = await UserProfiles.SearchAsync(connection, search ?? "", Limit, cancellationToken);
        return Results.Ok(new UserPickerResponse([.. profiles.Select(profile => new UserPickerItem(profile.Id, profile.DisplayName))]));
    }
}
