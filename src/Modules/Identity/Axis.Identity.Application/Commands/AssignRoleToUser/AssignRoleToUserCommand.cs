using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.AssignRoleToUser;

/// <summary>Assign or remove a role from a user within a tenant.</summary>
public sealed record AssignRoleToUserCommand(
    Guid UserId,
    Guid tenantId,
    Guid RoleId,
    RoleAction Action) : ICommand;
