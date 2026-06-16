using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.UpdateDataClass;

/// <summary>Validates name uniqueness (excluding self), then updates the data class.</summary>
public sealed class UpdateDataClassHandler(
    IDataClassRepository dataClassRepo,
    IUnitOfWork uow)
    : ICommandHandler<UpdateDataClassCommand>
{
    public async Task<Result> Handle(UpdateDataClassCommand command, CancellationToken cancellationToken)
    {
        DataClass? dc = await dataClassRepo.GetByIdAsync(command.DataClassId, command.OrganizationId, cancellationToken);
        if (dc is null)
            return Result.Failure(ErrorCodes.NotFound, "Data class not found.");

        if (await dataClassRepo.NameExistsAsync(command.Name, command.OrganizationId, command.DataClassId, cancellationToken))
            return Result.Failure(ErrorCodes.Conflict, $"A data class named '{command.Name}' already exists.");

        try
        {
            dc.Update(command.Name, command.Description);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
