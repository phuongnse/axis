using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.DeleteModel;

/// <summary>US-033: Validates model exists, then soft-deletes it.</summary>
public sealed class DeleteModelHandler(
    IDataModelRepository modelRepo,
    IUnitOfWork uow)
    : ICommandHandler<DeleteModelCommand>
{
    public async Task Handle(DeleteModelCommand command, CancellationToken cancellationToken)
    {
        var model = await modelRepo.GetByIdAsync(command.ModelId, command.OrganizationId, cancellationToken);
        if (model is null)
            throw new ValidationException("Model not found.");

        model.Delete();
        await uow.SaveChangesAsync(cancellationToken);
    }
}
