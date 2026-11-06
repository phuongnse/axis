using Axis.Core.Tenancy;
using Axis.Server.Http;

namespace Axis.Server.Tenancy;

/// <summary>
/// The refusal of every write in a read-only tenant, see <c>docs/reference/subscriptions.md#the-read-only-tenant</c>.
/// </summary>
internal static class ReadOnlyTenant
{
    // Sign-in, the account actions (sign-in with kc_action) and the session list are GETs, which are never refused.
    // The OIDC callback is answered inside authentication, before this check.
    /// <summary>The routes that still accept a write in a read-only tenant, as <c>METHOD pattern</c>.</summary>
    internal static readonly HashSet<string> OpenWrites =
    [
        "POST /api/auth/sign-out",
        "DELETE /api/me/sessions",
        "DELETE /api/me/sessions/{id}",
        "POST /api/tenant/exports",
    ];

    /// <summary>
    /// Refuses a <c>POST</c>, <c>PUT</c>, <c>PATCH</c> or <c>DELETE</c> in a read-only tenant with the <see cref="Problem"/>
    /// unless the route is in <see cref="OpenWrites"/>. The check looks at the method and never at the route, so a new write
    /// route is covered, and it runs before the route, so the request body is never read.
    /// </summary>
    public static IApplicationBuilder UseReadOnlyTenant(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var accessor = context.RequestServices.GetRequiredService<ITenantContextAccessor>();
            if (accessor.Current is { ReadOnly: true }
                && CsrfHeader.IsUnsafe(context.Request.Method)
                && !OpenWrites.Contains(Key(context)))
            {
                await Problem().ExecuteAsync(context);
                return;
            }

            await next(context);
        });

    /// <summary>The <c>403</c> problem details with the <c>reason</c> <c>tenant-read-only</c>. The title never names the tenant.</summary>
    public static IResult Problem() =>
        Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "This workspace is read-only.",
            extensions: new Dictionary<string, object?> { ["reason"] = "tenant-read-only" });

    private static string Key(HttpContext context) =>
        $"{context.Request.Method} {(context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText?.TrimEnd('/')}";
}
