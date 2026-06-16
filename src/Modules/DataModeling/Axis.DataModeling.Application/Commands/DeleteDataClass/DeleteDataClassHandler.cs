using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.DeleteDataClass;

/// <summary>Blocks deletion if the data class is referenced by any model field (HTTP 409).</summary>
public sealed class DeleteDataClassHandler(
    IDataClassRepository dataClassRepo,
    IUnitOfWork uow)
    : ICommandHandler<DeleteDataClassCommand>
{
    public async Task<Result> Handle(DeleteDataClassCommand command, CancellationToken cancellationToken)
    {
        DataClass? dc = await dataClassRepo.GetByIdAsync(command.DataClassId, command.OrganizationId, cancellationToken);
        if (dc is null)
            return Result.Failure(ErrorCodes.NotFound, "Data class not found.");

        // cannot delete while referenced by a model field
        if (await dataClassRepo.IsReferencedByAnyModelAsync(command.DataClassId, cancellationToken))
            return Result.Failure(ErrorCodes.Conflict,
                "This data class is still referenced by one or more model fields. Remove those fields first.");

        dc.Delete();
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
