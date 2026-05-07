using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.Identity.Application.Commands.DeactivateUser;

public sealed class DeactivateUserHandler(
    IUserRepository userRepo,
    IUnitOfWork uow)
    : ICommandHandler<DeactivateUserCommand>
{
    public async Task Handle(DeactivateUserCommand command, CancellationToken cancellationToken)
    {
        // US-019: cannot deactivate yourself
        if (command.UserId == command.RequesterId)
            throw new ValidationException("You cannot deactivate yourself.");

        var user = await userRepo.GetByIdAsync(command.UserId, command.OrganizationId, cancellationToken);
        if (user is null)
            throw new ValidationException("User not found.");

        // US-019: cannot deactivate last admin
        var adminCount = await userRepo.CountAdminsAsync(
            command.OrganizationId, command.AdminRoleId, cancellationToken);
        if (adminCount <= 1 && user.RoleIds.Contains(command.AdminRoleId))
            throw new ValidationException("You cannot deactivate the last admin of the organization.");

        user.Deactivate();
        await uow.SaveChangesAsync(cancellationToken);
    }
}
