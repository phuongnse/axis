namespace Axis.Server.Http;

/// <summary>The warning for a request that the server refuses with a <c>403</c>.</summary>
internal static partial class RefusedRequests
{
    /// <summary>
    /// Writes a warning with the HTTP method and the route template for every <c>403</c> the pipeline after it answers,
    /// whether a grant, a record filter, the CSRF header, the session or a Studio role refused the request. The route is
    /// the template, such as <c>/api/apps/{app}/entities/{entity}/records</c>, and never the path or the query string, so
    /// the entry holds no value. It sits after tenant resolution, so the entry carries the <c>TenantId</c> scope.
    /// </summary>
    public static IApplicationBuilder UseRefusedRequestLog(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            await next(context);
            if (context.Response.StatusCode == StatusCodes.Status403Forbidden)
            {
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RefusedRequests).FullName!);
                Refused(logger, context.Request.Method, (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(no route)");
            }
        });

    [LoggerMessage(LogLevel.Warning, "A {Method} request to {Route} was refused with 403.")]
    private static partial void Refused(ILogger logger, string method, string route);
}
