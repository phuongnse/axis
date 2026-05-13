using Serilog.Context;

namespace Axis.Api.Middleware;

/// <summary>
/// Reads X-Correlation-Id from the request (or generates a new GUID if absent),
/// echoes it on the response, and pushes it into Serilog's LogContext so every
/// log entry in the request scope is enriched with CorrelationId.
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString();

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
