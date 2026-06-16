using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.AssignRoleToUser;

/// <summary>Assign or remove a role from a user within an org.</summary>
public sealed record AssignRoleToUserCommand(
    Guid UserId,
    Guid OrganizationId,
    Guid RoleId,
    RoleAction Action) : ICommand;
