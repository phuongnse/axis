using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.CreateRole;

public sealed class CreateRoleHandler(
    IRoleRepository roleRepo,
    IUnitOfWork uow)
    : ICommandHandler<CreateRoleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        // at least one permission required
        if (command.Permissions.Count == 0)
            return Result.Failure<Guid>(ErrorCodes.BusinessRule, "A role must have at least one permission.");

        // unique name (case-insensitive) within the team account
        if (await roleRepo.NameExistsAsync(command.Name, command.TeamAccountId, null, cancellationToken))
            return Result.Failure<Guid>(ErrorCodes.Conflict, "A role with this name already exists.");

        Role role = Role.Create(command.Name, command.Description, command.TeamAccountId, command.Permissions);
        await roleRepo.AddAsync(role, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        return role.Id;
    }
}
