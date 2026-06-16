using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.InviteUser;

public record InviteUserCommand(
    Guid OrganizationId,
    string Email,
    Guid RoleId,
    Guid InvitedById) : ICommand;
