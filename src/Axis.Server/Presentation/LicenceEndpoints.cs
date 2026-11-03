using Axis.Server.Http;
using Axis.Server.Users;

namespace Axis.Server.Presentation;

/// <summary>
/// <c>GET /api/licences</c> gives a signed-in person the list of open source packages the server ships, with each version, from the
/// <c>licences.json</c> that <c>scripts/build.sh</c> writes into the content root. Without a session it answers 401, so an anonymous
/// visitor never learns the versions. A server built without that script answers 404.
/// </summary>
internal static class LicenceEndpoints
{
    public static void MapLicenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/licences", async (HttpContext context, IWebHostEnvironment environment) =>
        {
            if (await CurrentUsers.FindAsync(context) is null)
            {
                return Grants.SignInRequired();
            }

            var path = Path.Combine(environment.ContentRootPath, "licences.json");
            return File.Exists(path)
                ? Results.File(path, "application/json")
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The licence list was not built.");
        });
    }
}
