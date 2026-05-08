using Microsoft.AspNetCore.Authorization;

namespace Axis.Api.Authorization;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
