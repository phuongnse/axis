using Axis.Shared.Application.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Axis.Shared.Infrastructure.Tenancy;

/// <summary>
/// Resolves tenant information from the current HTTP request's JWT claims.
/// Requires an "tenant_id" claim (Guid) — included in the JWT issued by OpenIddict at POST /connect/token.
/// Schema name is derived from tenant_id so it is stable across Tenant renames.
/// </summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid tenantId
    {
        get
        {
            HttpContext ctx = accessor.HttpContext
                ?? throw new InvalidOperationException("No HTTP context available for tenant resolution.");

            string value = ctx.User.FindFirst("tenant_id")?.Value
                ?? throw new InvalidOperationException("JWT is missing required tenant_id claim.");

            return Guid.Parse(value);
        }
    }

    public string SchemaName => $"tenant_{tenantId:N}";
}
