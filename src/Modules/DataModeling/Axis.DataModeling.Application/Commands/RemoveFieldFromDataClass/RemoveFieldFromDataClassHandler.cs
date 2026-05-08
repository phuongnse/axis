using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.RemoveFieldFromDataClass;

/// <summary>US-039: Loads the data class and removes the specified field.</summary>
public sealed class RemoveFieldFromDataClassHandler(
    IDataClassRepository dataClassRepo,
    IUnitOfWork uow)
    : ICommandHandler<RemoveFieldFromDataClassCommand>
{
    public async Task Handle(RemoveFieldFromDataClassCommand command, CancellationToken cancellationToken)
    {
        var dc = await dataClassRepo.GetByIdAsync(command.DataClassId, command.OrganizationId, cancellationToken);
        if (dc is null)
            throw new ValidationException("Data class not found.");

        try
        {
            dc.RemoveField(command.FieldId);
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException(ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
    }
}
