using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.RemoveFieldFromDataClass;

/// <summary>Loads the data class and removes the specified field.</summary>
public sealed class RemoveFieldFromDataClassHandler(
    IDataClassRepository dataClassRepo,
    IUnitOfWork uow)
    : ICommandHandler<RemoveFieldFromDataClassCommand>
{
    public async Task<Result> Handle(RemoveFieldFromDataClassCommand command, CancellationToken cancellationToken)
    {
        DataClass? dc = await dataClassRepo.GetByIdAsync(command.DataClassId, command.workspaceId, cancellationToken);
        if (dc is null)
            return Result.Failure(ErrorCodes.NotFound, "Data class not found.");

        try
        {
            dc.RemoveField(command.FieldId);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
