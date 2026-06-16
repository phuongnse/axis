using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.ReorderFields;

/// <summary>Loads model and delegates field reordering to the aggregate.</summary>
public sealed class ReorderFieldsHandler(
    IDataModelRepository modelRepo,
    IUnitOfWork uow)
    : ICommandHandler<ReorderFieldsCommand>
{
    public async Task<Result> Handle(ReorderFieldsCommand command, CancellationToken cancellationToken)
    {
        DataModeling.Domain.Aggregates.DataModel? model = await modelRepo.GetByIdAsync(command.ModelId, command.tenantId, cancellationToken);
        if (model is null)
            return Result.Failure(ErrorCodes.NotFound, "Model not found.");

        try
        {
            model.ReorderFields(command.OrderedFieldIds);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
