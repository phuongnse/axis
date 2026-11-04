using Axis.Core.Tenancy;
using Axis.Server.CellApi;
using Axis.Tenancy;

namespace Axis.Server.Tenancy;

/// <summary>
/// Resolves the tenant from the request host for every request except the health endpoints. A platform host of a cell,
/// see <see cref="CellHosts"/>, resolves no tenant and serves only the routes of the provisioning API. A tenant host
/// answers <c>404</c> for every path under <c>/api/cell</c>. A host without <see cref="CellHosts"/> registered has no
/// platform host.
/// </summary>
internal sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    TenantResolver resolver,
    ITenantContextAccessor accessor,
    ILogger<TenantResolutionMiddleware> logger,
    CellHosts? cellHosts = null)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Health probes come from the platform, not from a tenant, and must work for any host.
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        // A platform host of a cell belongs to no tenant. It serves only the routes of the provisioning API.
        if (cellHosts?.IsPlatformHost(context.Request.Host.Host) == true)
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<CellOperatorRoute>() is not null)
            {
                await next(context);
                return;
            }

            await Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nothing is served at this path on this host.")
                .ExecuteAsync(context);
            return;
        }

        // A tenant host never serves the provisioning API. This answers before any sign-in work.
        if (context.Request.Path.StartsWithSegments("/api/cell", StringComparison.OrdinalIgnoreCase))
        {
            await Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No API route matches this path.")
                .ExecuteAsync(context);
            return;
        }

        // Request.Host.Host has no port. The response names neither the requested host nor any tenant.
        if (!resolver.TryResolve(context.Request.Host.Host, out var tenant))
        {
            if (resolver.IsUnavailable(context.Request.Host.Host))
            {
                await Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "This tenant is not available.")
                    .ExecuteAsync(context);
                return;
            }

            await Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No tenant is configured for this host.")
                .ExecuteAsync(context);
            return;
        }

        accessor.Current = tenant;
        try
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenant.TenantId }))
            {
                await next(context);
            }
        }
        finally
        {
            accessor.Current = null;
        }
    }
}

internal static class TenantResolutionApplicationBuilderExtensions
{
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();
}
