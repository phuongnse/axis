using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.Identity.Application.Commands.UpdateRole;

/// <summary>US-023: System roles cannot be edited; duplicate names are rejected.</summary>
public sealed class UpdateRoleHandler(
    IRoleRepository roleRepo,
    IUnitOfWork uow)
    : ICommandHandler<UpdateRoleCommand>
{
    public async Task Handle(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await roleRepo.GetByIdAsync(command.RoleId, command.OrganizationId, cancellationToken);
        if (role is null)
            throw new ValidationException("Role not found.");

        // US-023: system roles cannot be edited
        if (role.IsSystem)
            throw new ValidationException("Cannot modify a system role.");

        // US-023: name must be unique within the org (excluding self)
        if (await roleRepo.NameExistsAsync(command.Name, command.OrganizationId, role.Id, cancellationToken))
            throw new ValidationException("A role with this name already exists.");

        role.Update(command.Name, command.Description, command.Permissions);
        await uow.SaveChangesAsync(cancellationToken);
    }
}
