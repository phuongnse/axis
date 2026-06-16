using System.Security.Claims;
using OpenIddict.Abstractions;

namespace Axis.Api.Infrastructure;

public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid UserId => Guid.Parse(
        Principal?.GetClaim(OpenIddictConstants.Claims.Subject)
        ?? throw new InvalidOperationException("No sub claim"));

    public Guid TenantId => Guid.Parse(
        Principal?.FindFirstValue("tenant_id")
        ?? throw new InvalidOperationException("No tenant_id claim"));

    public Guid? TenantIdOrNull
    {
        get
        {
            string? TenantId = Principal?.FindFirstValue("tenant_id");
            return Guid.TryParse(TenantId, out Guid id) ? id : null;
        }
    }

    public string Email =>
        Principal?.GetClaim(OpenIddictConstants.Claims.Email)
        ?? throw new InvalidOperationException("No email claim");

    /// <summary>
    /// The unique token identifier (jti) — used for access token blacklisting on sign-out.
    /// OpenIddict places the internal token ID in the <c>oi_tkn_id</c> private claim,
    /// but also emits the standard <c>jti</c> claim in the JWT header.
    /// </summary>
    public string Jti =>
        Principal?.FindFirstValue("jti")
        ?? Principal?.GetClaim(OpenIddictConstants.Claims.Private.TokenId)
        ?? throw new InvalidOperationException("No jti/oi_tkn_id claim");

    public IReadOnlyList<string> Permissions =>
        Principal?.FindAll("permissions").Select(c => c.Value).ToList()
        ?? [];
}
