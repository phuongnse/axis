using Axis.Server.Health;
using Axis.Server.Http;
using Axis.Server.Presentation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Axis.Server.Authentication;

/// <summary>
/// The open list and the authorization fallback (D111). A route that is not on the open list needs an authenticated
/// caller: a session, or a service account or machine caller token. A request without one is refused before the route runs,
/// so it never opens a tenant database. The open routes are the two health probes, <c>GET /api/site</c>,
/// <c>GET /api/texts/{locale}</c>, sign-in, sign-out and the SPA's pages. The policy names no scheme, because
/// <see cref="EndpointCaller.RequestScheme"/> already picks one for each request.
/// </summary>
internal static class RouteAuthorization
{
    public static IServiceCollection AddRouteAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, RouteRefusal>();
        return services;
    }

    /// <summary>
    /// Maps the open routes, which have no rate limit and need no session, except sign-in and sign-out, which count with
    /// the session partition. The groups are not nested, so the route patterns keep their text.
    /// </summary>
    public static void MapOpenRoutes(this IEndpointRouteBuilder app)
    {
        var open = app.MapGroup(string.Empty).WithoutRateLimit().AllowAnonymous();
        open.MapHealthEndpoints();
        open.MapPresentationEndpoints();

        app.MapGroup(string.Empty).WithRateLimit(Partition.Session).AllowAnonymous().MapAuthEndpoints();

        app.MapFallbackToFile("index.html").WithoutRateLimit().AllowAnonymous();
    }
}

/// <summary>
/// Writes the problem of a request the fallback refuses. The authentication scheme's challenge would write a 401 with no
/// body, so it is never called.
/// </summary>
internal sealed class RouteRefusal : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            var refusal = EndpointCaller.IsEndpointPath(context.Request.Path)
                ? Grants.EndpointTokenRequired(context)
                : Grants.SignInRequired(context);
            await refusal.ExecuteAsync(context);
        }
        else if (authorizeResult.Forbidden)
        {
            await Grants.Forbidden().ExecuteAsync(context);
        }
        else
        {
            await next(context);
        }
    }
}
