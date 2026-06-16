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

    public Guid? TeamAccountId
    {
        get
        {
            string? teamAccountId = Principal?.FindFirstValue("team_account_id");
            return Guid.TryParse(teamAccountId, out Guid id) ? id : null;
        }
    }
}
