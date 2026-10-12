using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace Axis.Server.Endpoints;

/// <summary>
/// The routes other systems call with a bearer token: the endpoint call and its status location.
/// They take no session and no CSRF header. A request without a valid token is a <c>401</c> with
/// <c>WWW-Authenticate: Bearer</c>, which never says which check failed.
/// </summary>
internal static class InboundEndpoints
{
    public static void MapInboundEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/apps/{app}/endpoints/{name}", CallAsync);
        endpoints.MapGet("/api/apps/{app}/endpoints/{name}/instances/{instanceId}", CallAsync);
    }

    // It takes the request rather than the context, so it binds as a route handler and its result is written.
    // The endpoint call issue replaces the 404 with the call, once endpoint resources exist.
    private static async Task<IResult> CallAsync(HttpRequest request)
    {
        var httpContext = request.HttpContext;
        var caller = await httpContext.AuthenticateAsync(EndpointCaller.Scheme);
        if (!caller.Succeeded || EndpointCaller.Read(caller.Principal) is null)
        {
            httpContext.Response.Headers.WWWAuthenticate = "Bearer";
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "A valid bearer token is needed to call this endpoint.");
        }

        return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No endpoint exists with this name.");
    }
}
