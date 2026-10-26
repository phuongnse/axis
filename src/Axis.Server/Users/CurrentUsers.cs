using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace Axis.Server.Users;

/// <summary>
/// Finds the current user of a request. The record routes, the data source endpoint, process starts, process inspection,
/// task routes, the site endpoints and <c>GET /api/me</c> all read the user here. A service account route with
/// a bearer token reads the service account from the token alone and never touches the session. Every
/// other request reads the session.
/// </summary>
internal static class CurrentUsers
{
    /// <summary>The service account of a valid bearer token or the session user, or null when there is neither.</summary>
    public static async Task<CurrentUserResponse?> FindAsync(HttpContext httpContext)
    {
        if (ServiceAccount.Applies(httpContext))
        {
            var account = await httpContext.AuthenticateAsync(ServiceAccount.Scheme);
            return account.Succeeded && EndpointCaller.Read(account.Principal) is { } caller
                ? new CurrentUserResponse(caller.Id, caller.DisplayName, caller.Roles)
                : null;
        }

        var session = await httpContext.AuthenticateAsync(Session.Scheme);
        return session.Succeeded ? Session.Read(session.Principal) : null;
    }
}
