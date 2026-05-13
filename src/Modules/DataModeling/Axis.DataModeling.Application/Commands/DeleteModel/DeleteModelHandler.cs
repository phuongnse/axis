using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.DeleteModel;

/// <summary>US-033: Validates model exists, then soft-deletes it.</summary>
public sealed class DeleteModelHandler(
    IDataModelRepository modelRepo,
    IUnitOfWork uow)
    : ICommandHandler<DeleteModelCommand>
{
    public async Task<Result> Handle(DeleteModelCommand command, CancellationToken cancellationToken)
    {
        DataModeling.Domain.Aggregates.DataModel? model = await modelRepo.GetByIdAsync(command.ModelId, command.OrganizationId, cancellationToken);
        if (model is null)
            return Result.Failure(ErrorCodes.NotFound, "Model not found.");

        model.Delete();
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
