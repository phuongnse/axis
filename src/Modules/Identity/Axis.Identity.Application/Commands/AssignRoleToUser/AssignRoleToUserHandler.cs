using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.Identity.Application.Commands.AssignRoleToUser;

/// <summary>
/// US-024: Assigns or removes a role.
/// Guards: role must exist in org; user must retain at least one role; last admin cannot be stripped of Admin role.
/// </summary>
public sealed class AssignRoleToUserHandler(
    IUserRepository userRepo,
    IRoleRepository roleRepo,
    IUnitOfWork uow)
    : ICommandHandler<AssignRoleToUserCommand>
{
    public async Task Handle(AssignRoleToUserCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepo.GetByIdAsync(command.UserId, command.OrganizationId, cancellationToken);
        if (user is null)
            throw new ValidationException("User not found.");

        var role = await roleRepo.GetByIdAsync(command.RoleId, command.OrganizationId, cancellationToken);
        if (role is null)
            throw new ValidationException("The role was not found in this organization.");

        if (command.Action == RoleAction.Assign)
        {
            user.AssignRole(command.RoleId);
        }
        else
        {
            // US-024: a user must always have at least one role
            if (user.RoleIds.Count <= 1 && user.RoleIds.Contains(command.RoleId))
                throw new ValidationException("User must have at least one role.");

            // US-024: last admin guard — cannot remove admin role from the last admin
            if (role.IsSystem && role.Name == "Admin")
            {
                var adminCount = await userRepo.CountAdminsAsync(
                    command.OrganizationId, command.RoleId, cancellationToken);
                if (adminCount <= 1)
                    throw new ValidationException("Cannot remove the last admin role from this organization.");
            }

            user.RemoveRole(command.RoleId);
        }

        await uow.SaveChangesAsync(cancellationToken);
    }
}
