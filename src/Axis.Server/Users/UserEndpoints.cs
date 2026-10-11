namespace Axis.Server.Users;

/// <summary>
/// The current user, from the session. With nobody signed in it is a 401 problem.
/// </summary>
internal static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me", GetCurrentUserAsync);
    }

    // It takes the request rather than the context, so it binds as a route handler and its result is written.
    private static async Task<IResult> GetCurrentUserAsync(HttpRequest request) =>
        await CurrentUsers.FindAsync(request.HttpContext) is { } user
            ? Results.Ok(user)
            : Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Nobody is signed in.");
}

/// <summary>The signed-in user's id, display name and role names.</summary>
internal sealed record CurrentUserResponse(string Id, string DisplayName, IReadOnlyList<string> Roles);
