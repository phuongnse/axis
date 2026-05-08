using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.DeleteDataClass;

/// <summary>US-040: Blocks deletion if the data class is referenced by any model field (HTTP 409).</summary>
public sealed class DeleteDataClassHandler(
    IDataClassRepository dataClassRepo,
    IUnitOfWork uow)
    : ICommandHandler<DeleteDataClassCommand>
{
    public async Task Handle(DeleteDataClassCommand command, CancellationToken cancellationToken)
    {
        var dc = await dataClassRepo.GetByIdAsync(command.DataClassId, command.OrganizationId, cancellationToken);
        if (dc is null)
            throw new ValidationException("Data class not found.");

        // US-040: cannot delete while referenced by a model field
        if (await dataClassRepo.IsReferencedByAnyModelAsync(command.DataClassId, cancellationToken))
            throw new ValidationException(
                "This data class is still referenced by one or more model fields. Remove those fields first.");

        dc.Delete();
        await uow.SaveChangesAsync(cancellationToken);
    }
}
