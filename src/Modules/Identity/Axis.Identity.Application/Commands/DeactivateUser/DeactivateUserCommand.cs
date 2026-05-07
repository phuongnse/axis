using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.DeactivateUser;

public record DeactivateUserCommand(
    Guid UserId,
    Guid OrganizationId,
    Guid RequesterId,
    Guid AdminRoleId) : ICommand;
