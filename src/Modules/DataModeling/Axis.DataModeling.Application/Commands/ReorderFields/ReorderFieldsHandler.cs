using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.ReorderFields;

/// <summary>US-036: Loads model and delegates field reordering to the aggregate.</summary>
public sealed class ReorderFieldsHandler(
    IDataModelRepository modelRepo,
    IUnitOfWork uow)
    : ICommandHandler<ReorderFieldsCommand>
{
    public async Task Handle(ReorderFieldsCommand command, CancellationToken cancellationToken)
    {
        var model = await modelRepo.GetByIdAsync(command.ModelId, command.OrganizationId, cancellationToken);
        if (model is null)
            throw new ValidationException("Model not found.");

        try
        {
            model.ReorderFields(command.OrderedFieldIds);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
    }
}
