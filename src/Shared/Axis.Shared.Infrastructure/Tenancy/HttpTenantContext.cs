using Axis.Shared.Application.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Axis.Shared.Infrastructure.Tenancy;

/// <summary>
/// Resolves tenant information from the current HTTP request's JWT claims.
/// JWT must contain "org_id" (Guid) and "org_slug" (string) claims — both
/// are written by OpenIddict during token issuance.
/// </summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid OrganizationId
    {
        get
        {
            var ctx = accessor.HttpContext
                ?? throw new InvalidOperationException("No HTTP context available for tenant resolution.");

            var value = ctx.User.FindFirst("org_id")?.Value
                ?? throw new InvalidOperationException("JWT is missing required org_id claim.");

            return Guid.Parse(value);
        }
    }

    public string OrganizationSlug
    {
        get
        {
            var ctx = accessor.HttpContext
                ?? throw new InvalidOperationException("No HTTP context available for tenant resolution.");

            return ctx.User.FindFirst("org_slug")?.Value
                ?? throw new InvalidOperationException("JWT is missing required org_slug claim.");
        }
    }

    public string SchemaName => $"tenant_{OrganizationSlug}";
}
