using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.DeactivateUser;

public record DeactivateUserCommand(
    Guid UserId,
    Guid tenantId,
    Guid RequesterId) : ICommand;
