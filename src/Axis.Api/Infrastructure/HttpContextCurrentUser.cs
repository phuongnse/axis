using System.Security.Claims;
using Axis.Shared.Application.Identity;
using OpenIddict.Abstractions;

namespace Axis.Api.Infrastructure;

public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            string? sub = Principal?.GetClaim(OpenIddictConstants.Claims.Subject)
                ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(sub, out Guid id) ? id : null;
        }
    }

    public Guid? OrganizationId
    {
        get
        {
            string? orgId = Principal?.FindFirstValue("org_id");
            return Guid.TryParse(orgId, out Guid id) ? id : null;
        }
    }
}
