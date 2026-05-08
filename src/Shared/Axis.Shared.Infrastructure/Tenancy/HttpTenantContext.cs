using Axis.Shared.Application.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Axis.Shared.Infrastructure.Tenancy;

/// <summary>
/// Resolves tenant information from the current HTTP request's JWT claims.
/// Requires an "org_id" claim (Guid) — written by JwtTokenService at sign-in.
/// Schema name is derived from org_id so it is stable across org renames.
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

    public string SchemaName => $"tenant_{OrganizationId:N}";
}
