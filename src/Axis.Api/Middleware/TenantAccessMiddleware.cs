using Axis.Api.Infrastructure;
using Axis.Identity.Application.Services;
using Axis.Shared.Domain.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Middleware;

/// <summary>
/// Rejects tenant-scoped API calls when the JWT <c>tenant_id</c> references a missing,
/// suspended, or not-yet-ready Tenant.
/// </summary>
internal sealed class TenantAccessMiddleware(RequestDelegate next)
{
    private const string TenantIdClaim = "tenant_id";

    public async Task InvokeAsync(
        HttpContext context,
        ITenantAccessService accessService)
    {
        if (!TenantDataApiPaths.RequiresTenantDataAccess(context.Request.Path)
            || context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        string? TenantIdValue = context.User.FindFirst(TenantIdClaim)?.Value;
        if (string.IsNullOrEmpty(TenantIdValue) || !Guid.TryParse(TenantIdValue, out Guid tenantId))
        {
            await next(context);
            return;
        }

        Result access = await accessService.EvaluateAsync(tenantId, context.RequestAborted);

        if (access.IsSuccess)
        {
            await next(context);
            return;
        }

        ProblemDetails problem = new()
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Forbidden",
            Detail = access.Error,
        };

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
    }
}
