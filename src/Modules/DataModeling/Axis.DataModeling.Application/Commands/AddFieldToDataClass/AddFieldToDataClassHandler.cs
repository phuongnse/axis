using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Entities;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.AddFieldToDataClass;

/// <summary>/039: Loads the data class and delegates field creation to the aggregate.</summary>
public sealed class AddFieldToDataClassHandler(
    IDataClassRepository dataClassRepo,
    IUnitOfWork uow)
    : ICommandHandler<AddFieldToDataClassCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddFieldToDataClassCommand command, CancellationToken cancellationToken)
    {
        DataClass? dc = await dataClassRepo.GetByIdAsync(command.DataClassId, command.OrganizationId, cancellationToken);
        if (dc is null)
            return Result.Failure<Guid>(ErrorCodes.NotFound, "Data class not found.");

        try
        {
            FieldDefinition field = dc.AddField(command.Name, command.Label, command.Type, command.IsRequired, command.Config);
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
