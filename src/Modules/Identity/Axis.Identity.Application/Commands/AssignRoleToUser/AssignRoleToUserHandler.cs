using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.AssignRoleToUser;

/// <summary>
/// Assigns or removes a role.
/// Guards: role must exist in team account; user must retain at least one role; last admin cannot be stripped of Admin role.
/// </summary>
public sealed class AssignRoleToUserHandler(
    IUserRepository userRepo,
    ITeamAccountMembershipRepository membershipRepo,
    IRoleRepository roleRepo,
    IUnitOfWork uow)
    : ICommandHandler<AssignRoleToUserCommand>
{
    public async Task<Result> Handle(AssignRoleToUserCommand command, CancellationToken cancellationToken)
    {
        User? user = await userRepo.GetByIdAsync(command.UserId, command.TeamAccountId, cancellationToken);
        if (user is null)
            return Result.Failure(ErrorCodes.NotFound, "User not found.");

        TeamAccountMembership? membership = await membershipRepo.GetByUserAndTeamAccountAsync(
            command.UserId,
            command.TeamAccountId,
            cancellationToken);
        if (membership is null)
            return Result.Failure(ErrorCodes.NotFound, "Membership not found.");

        Role? role = await roleRepo.GetByIdAsync(command.RoleId, command.TeamAccountId, cancellationToken);
        if (role is null)
            return Result.Failure(ErrorCodes.NotFound, "The role was not found in this team account.");

        if (command.Action == RoleAction.Assign)
        {
            membership.AssignRole(command.RoleId);
        }
        else
        {
            // a user must always have at least one role
            if (membership.RoleIds.Count <= 1 && membership.RoleIds.Contains(command.RoleId))
                return Result.Failure(ErrorCodes.BusinessRule, "User must have at least one role.");

            // Last admin guard belongs to team account membership, not the user account.
            if (role.IsSystem && role.Name == "Admin")
            {
                int adminCount = await membershipRepo.CountAdminsAsync(
                    command.TeamAccountId, command.RoleId, cancellationToken);
                if (adminCount <= 1)
                    return Result.Failure(ErrorCodes.BusinessRule, "Cannot remove the last admin role from this team account.");
            }

            membership.RemoveRole(command.RoleId);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
