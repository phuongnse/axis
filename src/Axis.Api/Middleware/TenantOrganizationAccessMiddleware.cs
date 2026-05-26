using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;

namespace Axis.Api.Middleware;

/// <summary>
/// Rejects authenticated API traffic when the JWT <c>org_id</c> is missing, invalid,
/// or references an organization that no longer allows access (US-008 / US-009).
/// </summary>
public sealed class TenantOrganizationAccessMiddleware(
    RequestDelegate next,
    ILogger<TenantOrganizationAccessMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        IOrganizationRepository organizationRepository,
        CancellationToken cancellationToken)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        string? orgIdClaim = context.User.FindFirstValue("org_id");
        if (string.IsNullOrEmpty(orgIdClaim))
        {
            await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "Missing organization context.");
            return;
        }

        if (!Guid.TryParse(orgIdClaim, out Guid organizationId))
        {
            await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "Organization access denied.");
            return;
        }

        Organization? organization = await organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null || !organization.AllowsSignIn())
        {
            logger.LogWarning(
                "Tenant access denied for organization {OrganizationId} (status: {Status})",
                organizationId,
                organization?.Status.ToString() ?? "not found");

            await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "Organization access denied.");
            return;
        }

        await next(context);
    }

    private static Task WriteProblemAsync(HttpContext context, int statusCode, string detail)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(
            new { detail },
            cancellationToken: context.RequestAborted);
    }
}
