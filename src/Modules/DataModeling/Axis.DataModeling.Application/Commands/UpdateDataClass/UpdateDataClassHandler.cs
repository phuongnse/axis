using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.UpdateDataClass;

/// <summary>US-039: Validates name uniqueness (excluding self), then updates the data class.</summary>
public sealed class UpdateDataClassHandler(
    IDataClassRepository dataClassRepo,
    IUnitOfWork uow)
    : ICommandHandler<UpdateDataClassCommand>
{
    public async Task Handle(UpdateDataClassCommand command, CancellationToken cancellationToken)
    {
        var dc = await dataClassRepo.GetByIdAsync(command.DataClassId, command.OrganizationId, cancellationToken);
        if (dc is null)
            throw new ValidationException("Data class not found.");

        if (await dataClassRepo.NameExistsAsync(command.Name, command.OrganizationId, command.DataClassId, cancellationToken))
            throw new ValidationException($"A data class named '{command.Name}' already exists.");

        try
        {
            dc.Update(command.Name, command.Description);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
    }
}
