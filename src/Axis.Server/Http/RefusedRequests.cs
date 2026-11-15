using Axis.Server.CellApi;
using Microsoft.AspNetCore.WebUtilities;

namespace Axis.Server.Http;

/// <summary>
/// The warnings for a request that the server refuses: every <c>403</c>, and a <c>4xx</c> that the provisioning API refuses
/// for a signed-in operator, see <c>docs/reference/tenant-lifecycle.md#provisioning-api</c>.
/// </summary>
internal static partial class RefusedRequests
{
    private static readonly object _reasonKey = new();

    /// <summary>
    /// Answers a problem and stores its reason for the refused call warning. The reason is a fixed text that never holds a value
    /// of the request, and it is the title unless the title quotes a value, see
    /// <c>docs/reference/tenant-lifecycle.md#provisioning-api</c>.
    /// </summary>
    public static IResult Problem(HttpContext context, int statusCode, string title, string? reason = null)
    {
        context.Items[_reasonKey] = reason ?? title;
        return Results.Problem(statusCode: statusCode, title: title);
    }

    /// <summary>
    /// Writes a warning with the HTTP method and the route template for every <c>403</c> the pipeline after it answers,
    /// whether a grant, a record filter, the CSRF header, the session or a Studio role refused the request. It writes a second
    /// kind of warning for a <c>4xx</c> other than <c>401</c>, <c>403</c> and <c>429</c> on a route of the provisioning API
    /// when the caller is signed in: the method, the route template, the caller's id, the status and the fixed reason. The route
    /// is the template, such as <c>/api/apps/{app}/entities/{entity}/records</c>, and never the path or the query string in the
    /// message, so the message holds no value. The framework's <c>RequestPath</c> scope still holds the path. It sits after tenant resolution, so the entry carries the <c>TenantId</c> scope.
    /// </summary>
    public static IApplicationBuilder UseRefusedRequestLog(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            await next(context);
            var status = context.Response.StatusCode;
            if (status == StatusCodes.Status403Forbidden)
            {
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RefusedRequests).FullName!);
                Refused(logger, context.Request.Method, RouteOf(context));
            }
            else if (status is >= 400 and < 500 and not StatusCodes.Status401Unauthorized and not StatusCodes.Status429TooManyRequests
                && context.GetEndpoint()?.Metadata.GetMetadata<CellOperatorRoute>() is not null
                && context.User.FindFirst(CellOperator.IdClaim)?.Value is { } caller)
            {
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RefusedRequests).FullName!);
                RefusedProvisioningCall(
                    logger,
                    context.Request.Method,
                    RouteOf(context),
                    caller,
                    status,
                    context.Items[_reasonKey] as string ?? ReasonPhrases.GetReasonPhrase(status));
            }
        });

    private static string RouteOf(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(no route)";

    [LoggerMessage(LogLevel.Warning, "A {Method} request to {Route} was refused with 403.")]
    private static partial void Refused(ILogger logger, string method, string route);

    [LoggerMessage(LogLevel.Warning, "A {Method} call to {Route} by operator {CallerId} was refused with {Status}. {Reason}")]
    private static partial void RefusedProvisioningCall(ILogger logger, string method, string route, string callerId, int status, string reason);
}
