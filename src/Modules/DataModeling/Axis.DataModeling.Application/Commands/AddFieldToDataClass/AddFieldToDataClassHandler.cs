using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.AddFieldToDataClass;

/// <summary>US-037/039: Loads the data class and delegates field creation to the aggregate.</summary>
public sealed class AddFieldToDataClassHandler(
    IDataClassRepository dataClassRepo,
    IUnitOfWork uow)
    : ICommandHandler<AddFieldToDataClassCommand, Guid>
{
    public async Task<Guid> Handle(AddFieldToDataClassCommand command, CancellationToken cancellationToken)
    {
        var dc = await dataClassRepo.GetByIdAsync(command.DataClassId, command.OrganizationId, cancellationToken);
        if (dc is null)
            throw new ValidationException("Data class not found.");

        try
        {
            var field = dc.AddField(command.Name, command.Label, command.Type, command.IsRequired, command.Config);
            await uow.SaveChangesAsync(cancellationToken);
            return field.Id;
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException(ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(ex.Message);
        }
    }
}
