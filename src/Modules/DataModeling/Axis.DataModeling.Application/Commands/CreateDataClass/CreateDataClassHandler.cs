using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.CreateDataClass;

/// <summary>Validates uniqueness then creates the data class.</summary>
public sealed class CreateDataClassHandler(
    IDataClassRepository dataClassRepo,
    IUnitOfWork uow)
    : ICommandHandler<CreateDataClassCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateDataClassCommand command, CancellationToken cancellationToken)
    {
        if (await dataClassRepo.NameExistsAsync(command.Name, command.workspaceId, null, cancellationToken))
            return Result.Failure<Guid>(ErrorCodes.Conflict, $"A data class named '{command.Name}' already exists.");

        DataClass dataClass = DataClass.Create(command.Name, command.Description, command.workspaceId, command.CreatedBy);

        await dataClassRepo.AddAsync(dataClass, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        return dataClass.Id;
    }
}
