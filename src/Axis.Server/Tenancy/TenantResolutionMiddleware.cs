using Axis.Core.Tenancy;
using Axis.Tenancy;

namespace Axis.Server.Tenancy;

/// <summary>Resolves the tenant from the request host for every request except the health endpoints.</summary>
internal sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    TenantResolver resolver,
    ITenantContextAccessor accessor,
    ILogger<TenantResolutionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Health probes come from the platform, not from a tenant, and must work for any host.
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        // Request.Host.Host has no port. The response names neither the requested host nor any tenant.
        if (!resolver.TryResolve(context.Request.Host.Host, out var tenant))
        {
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
