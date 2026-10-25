using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>Every <c>/api</c> route and method of a test server, read from its endpoint data source.</summary>
internal static class ApiRoutes
{
    public static IEnumerable<ApiRoute> From(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .SelectMany(endpoint =>
            {
                var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
                return methods.Select(method => new ApiRoute(endpoint, method.ToUpperInvariant(), endpoint.RoutePattern));
            });
}

/// <summary>One method of one route, with the path that fills each parameter with <c>x</c>.</summary>
internal sealed record ApiRoute(RouteEndpoint Endpoint, string Method, RoutePattern Pattern)
{
    // A map group with an empty route has a trailing slash in its raw text.
    public string Name => $"{Method} {Pattern.RawText?.TrimEnd('/')}";

    public string Path => string.Concat(Pattern.PathSegments.Select(segment =>
        "/" + string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            _ => "x",
        }))));
}
