using Axis.Api.Infrastructure;
using Axis.Identity.Application.Services;
using Axis.Shared.Domain.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Middleware;

/// <summary>
/// Rejects tenant-scoped API calls when the JWT <c>org_id</c> references a missing,
/// suspended, or not-yet-ready organization.
/// </summary>
internal sealed class TenantOrganizationAccessMiddleware(RequestDelegate next)
{
    private const string OrgIdClaim = "org_id";

    public async Task InvokeAsync(
        HttpContext context,
        ITenantOrganizationAccessService accessService)
    {
        if (!TenantDataApiPaths.RequiresTenantDataAccess(context.Request.Path)
            || context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        string? orgIdValue = context.User.FindFirst(OrgIdClaim)?.Value;
        if (string.IsNullOrEmpty(orgIdValue) || !Guid.TryParse(orgIdValue, out Guid organizationId))
        {
            await next(context);
            return;
        }

        Result access = await accessService.EvaluateAsync(organizationId, context.RequestAborted);

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
