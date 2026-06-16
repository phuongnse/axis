using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.InviteUser;

public record InviteUserCommand(
    Guid tenantId,
    string Email,
    Guid RoleId,
    Guid InvitedById) : ICommand;
