using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.RemoveField;

/// <summary>US-032/034: Loads model and delegates field removal to the aggregate.</summary>
public sealed class RemoveFieldHandler(
    IDataModelRepository modelRepo,
    IUnitOfWork uow)
    : ICommandHandler<RemoveFieldCommand>
{
    public async Task Handle(RemoveFieldCommand command, CancellationToken cancellationToken)
    {
        var model = await modelRepo.GetByIdAsync(command.ModelId, command.OrganizationId, cancellationToken);
        if (model is null)
            throw new ValidationException("Model not found.");

        try
        {
            model.RemoveField(command.FieldId);
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException(ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
    }
}
