using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.DeactivateUser;

public sealed class DeactivateUserHandler(
    IUserRepository userRepo,
    IUnitOfWork uow)
    : ICommandHandler<DeactivateUserCommand>
{
    public async Task<Result> Handle(DeactivateUserCommand command, CancellationToken cancellationToken)
    {
        // US-019: cannot deactivate yourself
        if (command.UserId == command.RequesterId)
            return Result.Failure(ErrorCodes.BusinessRule, "You cannot deactivate yourself.");

        User? user = await userRepo.GetByIdAsync(command.UserId, command.OrganizationId, cancellationToken);
        if (user is null)
            return Result.Failure(ErrorCodes.NotFound, "User not found.");

        // US-019: cannot deactivate last admin
        int adminCount = await userRepo.CountAdminsAsync(
            command.OrganizationId, command.AdminRoleId, cancellationToken);
        if (adminCount <= 1 && user.RoleIds.Contains(command.AdminRoleId))
            return Result.Failure(ErrorCodes.BusinessRule, "You cannot deactivate the last admin of the organization.");

        user.Deactivate();
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
