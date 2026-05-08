using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.UpdateModel;

/// <summary>US-032: Validates name uniqueness (excluding self), then updates the model.</summary>
public sealed class UpdateModelHandler(
    IDataModelRepository modelRepo,
    IUnitOfWork uow)
    : ICommandHandler<UpdateModelCommand>
{
    public async Task Handle(UpdateModelCommand command, CancellationToken cancellationToken)
    {
        var model = await modelRepo.GetByIdAsync(command.ModelId, command.OrganizationId, cancellationToken);
        if (model is null)
            throw new ValidationException("Model not found.");

        if (await modelRepo.NameExistsAsync(command.Name, command.OrganizationId, command.ModelId, cancellationToken))
            throw new ValidationException($"A model named '{command.Name}' already exists.");

        try
        {
            model.Update(command.Name, command.Description, command.Icon, command.Color);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
    }
}
