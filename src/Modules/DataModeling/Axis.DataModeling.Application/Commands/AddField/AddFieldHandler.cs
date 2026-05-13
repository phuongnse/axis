using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Entities;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.AddField;

/// <summary>US-034: Loads the model and delegates field creation to the aggregate.</summary>
public sealed class AddFieldHandler(
    IDataModelRepository modelRepo,
    IUnitOfWork uow)
    : ICommandHandler<AddFieldCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddFieldCommand command, CancellationToken cancellationToken)
    {
        DataModel? model = await modelRepo.GetByIdAsync(command.ModelId, command.OrganizationId, cancellationToken);
        if (model is null)
            return Result.Failure<Guid>(ErrorCodes.NotFound, "Model not found.");

        try
        {
            FieldDefinition field = model.AddField(command.Name, command.Label, command.Type, command.IsRequired, command.Config);
            await uow.SaveChangesAsync(cancellationToken);
            return field.Id;
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<Guid>(ErrorCodes.BusinessRule, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<Guid>(ErrorCodes.BusinessRule, ex.Message);
        }
    }
}
