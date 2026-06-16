using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.DeactivateUser;

public record DeactivateUserCommand(
    Guid UserId,
    Guid TeamAccountId,
    Guid RequesterId) : ICommand;
