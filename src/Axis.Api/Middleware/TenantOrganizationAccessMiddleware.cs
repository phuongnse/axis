using Axis.Api.Infrastructure;
using Axis.Identity.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Middleware;

/// <summary>
/// Rejects tenant-scoped API calls when the JWT <c>org_id</c> references a missing,
/// suspended, or not-yet-ready organization (E01 F03 US-009).
/// </summary>
internal sealed class TenantOrganizationAccessMiddleware(
    RequestDelegate next,
    ITenantOrganizationAccessService accessService)
{
    private const string OrgIdClaim = "org_id";

    public async Task InvokeAsync(HttpContext context)
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

        TenantOrganizationAccessResult access =
            await accessService.EvaluateAsync(organizationId, context.RequestAborted);

        if (access.Status == TenantOrganizationAccessStatus.Allowed)
        {
            await next(context);
            return;
        }

        int statusCode = StatusCodes.Status403Forbidden;
        string detail = access.Status switch
        {
            TenantOrganizationAccessStatus.OrganizationNotFound =>
                "Organization is not available.",
            TenantOrganizationAccessStatus.OrganizationSuspended =>
                "Organization is not available.",
            TenantOrganizationAccessStatus.OrganizationNotReady =>
                "Workspace is still being set up. Try again shortly.",
            _ => "Organization is not available.",
        };

        ProblemDetails problem = new()
        {
            Status = statusCode,
            Title = "Forbidden",
            Detail = detail,
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(problem);
    }
}
