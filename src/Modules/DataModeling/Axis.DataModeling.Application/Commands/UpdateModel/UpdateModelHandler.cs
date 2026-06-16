using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.UpdateModel;

/// <summary>Validates name uniqueness (excluding self), then updates the model.</summary>
public sealed class UpdateModelHandler(
    IDataModelRepository modelRepo,
    IUnitOfWork uow)
    : ICommandHandler<UpdateModelCommand>
{
    public async Task<Result> Handle(UpdateModelCommand command, CancellationToken cancellationToken)
    {
        DataModeling.Domain.Aggregates.DataModel? model = await modelRepo.GetByIdAsync(command.ModelId, command.TeamAccountId, cancellationToken);
        if (model is null)
            return Result.Failure(ErrorCodes.NotFound, "Model not found.");

        if (await modelRepo.NameExistsAsync(command.Name, command.TeamAccountId, command.ModelId, cancellationToken))
            return Result.Failure(ErrorCodes.Conflict, $"A model named '{command.Name}' already exists.");

        try
        {
            model.Update(command.Name, command.Description, command.Icon, command.Color);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
