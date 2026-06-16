using System.Diagnostics;
using Serilog.Context;

namespace Axis.Api.Middleware;

/// <summary>
/// Reads X-Correlation-Id from the request (or the active trace ID from OpenTelemetry),
/// echoes it on the response, and pushes correlation + tenant fields into Serilog's
/// LogContext so every log entry in the request scope is queryable in Loki (ADR-018).
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-Id";
    private const string TenantIdClaim = "org_id";

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Activity.Current?.TraceId.ToString()
            ?? Guid.NewGuid().ToString();

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        Activity? activity = Activity.Current;
        activity?.SetTag("correlation.id", correlationId);

        string? tenantId = context.User.FindFirst(TenantIdClaim)?.Value;
        if (tenantId is not null)
            activity?.SetTag("tenant.id", tenantId);

        List<IDisposable> disposables = [LogContext.PushProperty("CorrelationId", correlationId)];
        if (tenantId is not null)
            disposables.Add(LogContext.PushProperty("TenantId", tenantId));

        try
        {
            await next(context);
        }
        finally
        {
            foreach (IDisposable disposable in disposables)
                disposable.Dispose();
        }
    }
}
