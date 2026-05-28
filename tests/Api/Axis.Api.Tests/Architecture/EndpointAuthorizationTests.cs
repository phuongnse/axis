using Axis.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Api.Tests.Architecture;

/// <summary>
/// Runtime convention check: every route endpoint must declare authorization
/// intent **explicitly** — either <c>.RequireAuthorization</c> (or an
/// <c>[Authorize]</c>-equivalent) so callers must authenticate, or
/// <c>.AllowAnonymous</c> (or <c>[AllowAnonymous]</c>) so the public access
/// is intentional and visible in code review.
///
/// <para>
/// Without this rule a typo or a forgotten chain in an endpoint registration
/// silently exposes the endpoint with whatever the application-wide default
/// policy is (currently: anonymous). Walking <see cref="EndpointDataSource"/>
/// catches the missing markers regardless of where in the registration chain
/// they belong (route group, individual endpoint, or attribute) because
/// ASP.NET propagates metadata down at build time.
/// </para>
///
/// <para>
/// Health checks, OpenAPI, Scalar, and the OpenIddict /connect endpoints
/// are excluded: their auth model lives outside the ASP.NET authorization
/// pipeline (custom middleware / framework-managed). The exclusion list is
/// kept narrow and tagged for review.
/// </para>
/// </summary>
[Collection("Api")]
public sealed class EndpointAuthorizationTests(ApiTestFixture fixture)
{
    /// <summary>
    /// Route patterns whose auth model is intentionally outside the standard
    /// ASP.NET authorization pipeline. Every entry needs a one-line reason
    /// next to it so the next agent reading this list can verify the call.
    /// </summary>
    private static readonly HashSet<string> AuthorizationModelExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenIddict — auth is enforced by OpenIddictServerHandlers in the
        // request pipeline, not by ASP.NET's [Authorize] metadata.
        "/connect/authorize",
        "/connect/login",
        "/connect/token",

        // Health probes — explicitly anonymous by ADR-021 (load balancers /
        // K8s liveness probes hit them without credentials).
        "/health",
        "/health/ready",

        // Prometheus scrape endpoint — exposed only on the metrics port in
        // production; access control via network policy, not [Authorize].
        "/metrics",
    };

    [Fact]
    public void EveryRouteEndpoint_WhenInspected_HasExplicitAuthOrAnonymousMarker()
    {
        using IServiceScope scope = fixture.CreateScope();
        EndpointDataSource dataSource = scope.ServiceProvider.GetRequiredService<EndpointDataSource>();

        List<string> violations = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => !IsExcluded(e))
            .Where(e => !HasExplicitAuthMarker(e))
            .Select(DescribeEndpoint)
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty(
            "Every endpoint must opt-in to authorization with .RequireAuthorization() OR opt-out " +
            "with .AllowAnonymous() so intent is visible in code review. Endpoints currently " +
            "missing both markers:\n  " + string.Join("\n  ", violations) +
            "\n\nFix one of:\n" +
            "  (a) add .RequireAuthorization() / [Authorize] to the endpoint or its route group;\n" +
            "  (b) add .AllowAnonymous() / [AllowAnonymous] if the endpoint is deliberately public;\n" +
            "  (c) add the route to AuthorizationModelExceptions in this test file IF its auth " +
            "model lives outside the ASP.NET authorization pipeline (rare — needs a one-line " +
            "justification comment).");
    }

    private static bool HasExplicitAuthMarker(RouteEndpoint endpoint)
    {
        EndpointMetadataCollection metadata = endpoint.Metadata;
        return metadata.GetMetadata<IAuthorizeData>() is not null
            || metadata.GetMetadata<IAllowAnonymous>() is not null;
    }

    private static bool IsExcluded(RouteEndpoint endpoint)
    {
        string? path = endpoint.RoutePattern.RawText;
        return path is not null && AuthorizationModelExceptions.Contains(path);
    }

    private static string DescribeEndpoint(RouteEndpoint endpoint)
    {
        string method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault()
            ?? "ANY";
        string path = endpoint.RoutePattern.RawText ?? "<unknown>";
        return $"{method,-6} {path}";
    }
}
