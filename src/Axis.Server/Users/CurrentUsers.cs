using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace Axis.Server.Users;

/// <summary>
/// Finds the current user of a request: the session user, or else the signed-in test user while
/// test users exist. Record writes, process starts, task routes and <c>GET /api/me</c> all read the
/// user here.
/// </summary>
internal static class CurrentUsers
{
    /// <summary>The session user, or else the signed-in test user, or null when nobody is signed in.</summary>
    public static async Task<CurrentUserResponse?> FindAsync(HttpContext httpContext, TestUserDirectory directory)
    {
        var session = await httpContext.AuthenticateAsync(Session.Scheme);
        if (session.Succeeded && Session.Read(session.Principal) is { } user)
        {
            return user;
        }

        return directory.FindSignedIn(httpContext.User) is { } id && directory.Find(id) is { } testUser
            ? new CurrentUserResponse(testUser.Id, testUser.DisplayName, testUser.Roles)
            : null;
    }
}
