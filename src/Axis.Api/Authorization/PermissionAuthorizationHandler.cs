using Microsoft.AspNetCore.Authorization;

namespace Axis.Api.Authorization;

internal sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var permissions = context.User
            .FindAll("permissions")
            .Select(c => c.Value)
            .ToHashSet();

        if (permissions.Contains(requirement.Permission))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
