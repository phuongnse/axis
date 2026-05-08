using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.UpdateField;

/// <summary>US-032/034: Loads model and delegates field update to the aggregate.</summary>
public sealed class UpdateFieldHandler(
    IDataModelRepository modelRepo,
    IUnitOfWork uow)
    : ICommandHandler<UpdateFieldCommand>
{
    public async Task Handle(UpdateFieldCommand command, CancellationToken cancellationToken)
    {
        var model = await modelRepo.GetByIdAsync(command.ModelId, command.OrganizationId, cancellationToken);
        if (model is null)
            throw new ValidationException("Model not found.");

        try
        {
            model.UpdateField(command.FieldId, command.Label, command.HelpText, command.IsRequired, command.Config);
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException(ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
    }
}
