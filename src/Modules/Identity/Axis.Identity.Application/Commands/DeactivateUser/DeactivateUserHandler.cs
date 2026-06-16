using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.DeactivateUser;

public sealed class DeactivateUserHandler(
    IUserRepository userRepo,
    ITenantMembershipRepository membershipRepo,
    IRoleRepository roleRepo,
    ISessionStore sessionStore,
    IUnitOfWork uow)
    : ICommandHandler<DeactivateUserCommand>
{
    public async Task<Result> Handle(DeactivateUserCommand command, CancellationToken cancellationToken)
    {
        // cannot deactivate yourself
        if (command.UserId == command.RequesterId)
            return Result.Failure(ErrorCodes.BusinessRule, "You cannot deactivate yourself.");

        User? user = await userRepo.GetByIdAsync(command.UserId, command.tenantId, cancellationToken);
        if (user is null)
            return Result.Failure(ErrorCodes.NotFound, "User not found.");

        TenantMembership? membership = await membershipRepo.GetByUserAndTenantAsync(
            command.UserId,
            command.tenantId,
            cancellationToken);
        if (membership is null)
            return Result.Failure(ErrorCodes.NotFound, "Membership not found.");

        Role? adminRole = await roleRepo.GetByNameAsync("Admin", command.tenantId, cancellationToken);
        if (adminRole is null)
            return Result.Failure(ErrorCodes.NotFound, "Admin role not found.");

        Guid adminRoleId = adminRole.Id;

        // Last admin is a membership-scoped invariant.
        int adminCount = await membershipRepo.CountAdminsAsync(
            command.tenantId, adminRoleId, cancellationToken);
        if (adminCount <= 1 && membership.RoleIds.Contains(adminRoleId))
            return Result.Failure(ErrorCodes.BusinessRule, "You cannot deactivate the last admin of the Tenant.");

        membership.Deactivate();
        await uow.SaveChangesAsync(cancellationToken);
        await sessionStore.RevokeAllAsync(command.UserId, cancellationToken);
        return Result.Success();
    }
}
