using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Axis.Worker;

public static class WorkerHealthEndpoints
{
    public const string LiveTag = "live";
    public const string ReadyTag = "ready";

    /// <summary>
    /// Listens on <c>Health:Urls</c> and serves <c>/health/live</c> and <c>/health/ready</c>. Every other
    /// path answers <c>404</c>.
    /// </summary>
    public static WebApplication MapWorkerHealth(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Urls.Clear();
        foreach (var url in app.Services.GetRequiredService<HealthOptions>().Urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            app.Urls.Add(url);
        }

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(LiveTag),
            ResponseWriter = WriteResponseAsync,
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = WriteResponseAsync,
        });
        return app;
    }

    // The answer names no check, tenant or error. Those stay in the logs.
    private static Task WriteResponseAsync(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(new HealthResponse(report.Status.ToString()));

    private sealed record HealthResponse(string Status);
}
