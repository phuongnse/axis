using System.Security.Claims;

namespace Axis.Api.Infrastructure;

public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid UserId => Guid.Parse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Principal?.FindFirstValue("sub")
        ?? throw new InvalidOperationException("No authenticated user"));

    public Guid OrgId => Guid.Parse(Principal?.FindFirstValue("org_id")
        ?? throw new InvalidOperationException("No org_id claim"));

    public string Email => Principal?.FindFirstValue(ClaimTypes.Email)
        ?? Principal?.FindFirstValue("email")
        ?? throw new InvalidOperationException("No email claim");

    public string? RefreshTokenId => Principal?.FindFirstValue("rt_id");

    public string Jti => Principal?.FindFirstValue("jti")
        ?? throw new InvalidOperationException("No jti claim");

    public IReadOnlyList<string> Permissions =>
        Principal?.FindAll("permissions").Select(c => c.Value).ToList()
        ?? [];
}
