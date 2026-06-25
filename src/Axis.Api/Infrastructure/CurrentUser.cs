using System.Security.Claims;
using OpenIddict.Abstractions;

namespace Axis.Api.Infrastructure;

public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid UserId => Guid.Parse(
        Principal?.GetClaim(OpenIddictConstants.Claims.Subject)
        ?? throw new InvalidOperationException("No sub claim"));

    public Guid WorkspaceId => Guid.Parse(
        Principal?.FindFirstValue("workspace_id")
        ?? throw new InvalidOperationException("No workspace_id claim"));

    public Guid? WorkspaceIdOrNull
    {
        get
        {
            string? WorkspaceId = Principal?.FindFirstValue("workspace_id");
            return Guid.TryParse(WorkspaceId, out Guid id) ? id : null;
        }
    }

    public string Email =>
        Principal?.GetClaim(OpenIddictConstants.Claims.Email)
        ?? throw new InvalidOperationException("No email claim");
}
