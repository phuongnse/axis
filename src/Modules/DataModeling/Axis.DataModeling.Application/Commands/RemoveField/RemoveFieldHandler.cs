using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.RemoveField;

/// <summary>/034: Loads model and delegates field removal to the aggregate.</summary>
public sealed class RemoveFieldHandler(
    IDataModelRepository modelRepo,
    IUnitOfWork uow)
    : ICommandHandler<RemoveFieldCommand>
{
    public async Task<Result> Handle(RemoveFieldCommand command, CancellationToken cancellationToken)
    {
        DataModeling.Domain.Aggregates.DataModel? model = await modelRepo.GetByIdAsync(command.ModelId, command.workspaceId, cancellationToken);
        if (model is null)
            return Result.Failure(ErrorCodes.NotFound, "Model not found.");

        try
        {
            model.RemoveField(command.FieldId);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
