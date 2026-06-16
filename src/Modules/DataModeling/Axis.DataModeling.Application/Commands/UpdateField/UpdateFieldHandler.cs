using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.UpdateField;

/// <summary>/034: Loads model and delegates field update to the aggregate.</summary>
public sealed class UpdateFieldHandler(
    IDataModelRepository modelRepo,
    IUnitOfWork uow)
    : ICommandHandler<UpdateFieldCommand>
{
    public async Task<Result> Handle(UpdateFieldCommand command, CancellationToken cancellationToken)
    {
        DataModeling.Domain.Aggregates.DataModel? model = await modelRepo.GetByIdAsync(command.ModelId, command.tenantId, cancellationToken);
        if (model is null)
            return Result.Failure(ErrorCodes.NotFound, "Model not found.");

        try
        {
            model.UpdateField(command.FieldId, command.Label, command.HelpText, command.IsRequired, command.Config);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
