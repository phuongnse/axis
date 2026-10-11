using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace Axis.Server.Users;

/// <summary>
/// Finds the current user of a request from the session. Record writes, process starts, task routes
/// and <c>GET /api/me</c> all read the user here.
/// </summary>
internal static class CurrentUsers
{
    /// <summary>The session user, or null when nobody is signed in.</summary>
    public static async Task<CurrentUserResponse?> FindAsync(HttpContext httpContext)
    {
        var session = await httpContext.AuthenticateAsync(Session.Scheme);
        return session.Succeeded ? Session.Read(session.Principal) : null;
    }
}
