using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.AddField;

/// <summary>US-034: Loads the model and delegates field creation to the aggregate.</summary>
public sealed class AddFieldHandler(
    IDataModelRepository modelRepo,
    IUnitOfWork uow)
    : ICommandHandler<AddFieldCommand, Guid>
{
    public async Task<Guid> Handle(AddFieldCommand command, CancellationToken cancellationToken)
    {
        var model = await modelRepo.GetByIdAsync(command.ModelId, command.OrganizationId, cancellationToken);
        if (model is null)
            throw new ValidationException("Model not found.");

        try
        {
            var field = model.AddField(command.Name, command.Label, command.Type, command.IsRequired, command.Config);
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
