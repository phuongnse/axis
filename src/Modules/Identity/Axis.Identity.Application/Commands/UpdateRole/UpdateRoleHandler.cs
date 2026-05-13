using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.UpdateRole;

/// <summary>US-023: System roles cannot be edited; duplicate names are rejected.</summary>
public sealed class UpdateRoleHandler(
    IRoleRepository roleRepo,
    IUnitOfWork uow)
    : ICommandHandler<UpdateRoleCommand>
{
    public async Task<Result> Handle(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        Role? role = await roleRepo.GetByIdAsync(command.RoleId, command.OrganizationId, cancellationToken);
        if (role is null)
            return Result.Failure(ErrorCodes.NotFound, "Role not found.");

        // US-023: system roles cannot be edited
        if (role.IsSystem)
            return Result.Failure(ErrorCodes.BusinessRule, "Cannot modify a system role.");

        // US-023: name must be unique within the org (excluding self)
        if (await roleRepo.NameExistsAsync(command.Name, command.OrganizationId, role.Id, cancellationToken))
            return Result.Failure(ErrorCodes.Conflict, "A role with this name already exists.");

        role.Update(command.Name, command.Description, command.Permissions);
        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
