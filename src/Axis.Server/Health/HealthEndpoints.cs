using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Axis.Server.Health;

/// <summary>Liveness and readiness endpoints.</summary>
internal static class HealthEndpoints
{
    public const string ReadyTag = "ready";

    public static void MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness runs no checks: it only shows that the process serves requests.
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteResponseAsync,
        });

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = WriteResponseAsync,
        });
    }

    // The answer names no check, tenant or error. Those stay in the logs.
    private static Task WriteResponseAsync(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(new HealthResponse(report.Status.ToString()));

    private sealed record HealthResponse(string Status);
}
