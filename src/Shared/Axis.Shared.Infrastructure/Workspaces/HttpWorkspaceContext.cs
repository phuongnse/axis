using Axis.Shared.Application.Workspaces;
using Microsoft.AspNetCore.Http;

namespace Axis.Shared.Infrastructure.Workspaces;

/// <summary>
/// Resolves workspace information from the current HTTP request's JWT claims.
/// Requires an "workspace_id" claim (Guid) — included in the JWT issued by OpenIddict at POST /connect/token.
/// Schema name is derived from workspace_id so it is stable across Workspace renames.
/// </summary>
public sealed class HttpWorkspaceContext(IHttpContextAccessor accessor) : IWorkspaceContext
{
    public Guid workspaceId
    {
        get
        {
            HttpContext ctx = accessor.HttpContext
                ?? throw new InvalidOperationException("No HTTP context available for workspace resolution.");

            string value = ctx.User.FindFirst("workspace_id")?.Value
                ?? throw new InvalidOperationException("JWT is missing required workspace_id claim.");

            return Guid.Parse(value);
        }
    }

    public string SchemaName => $"workspace_{workspaceId:N}";
}
