using System.Reflection;
using Axis.Server.Http;
using Axis.Server.Users;

namespace Axis.Server.Presentation;

/// <summary>
/// <c>GET /api/about</c> gives a signed-in person the server's informational version. Without a session it answers 401,
/// so an anonymous visitor never learns the version.
/// </summary>
internal static class AboutEndpoints
{
    private static readonly string _version =
        typeof(AboutEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    public static void MapAboutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/about", async (HttpContext context) =>
            await CurrentUsers.FindAsync(context) is null
                ? Grants.SignInRequired()
                : Results.Ok(new AboutResponse(_version)));
    }
}

/// <summary>The answer of <c>GET /api/about</c>.</summary>
/// <param name="Version">The server assembly's informational version.</param>
internal sealed record AboutResponse(string Version);
