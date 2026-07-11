using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Commands.SaveUnpublishedBusinessObjectDefinition;

public sealed class SaveUnpublishedBusinessObjectDefinitionHandler(
    ICurrentUser currentUser,
    IBusinessObjectDefinitionRepository repository,
    IUnitOfWork unitOfWork,
    IBusinessObjectDefinitionInputPlanner inputPlanner)
    : ICommandHandler<SaveUnpublishedBusinessObjectDefinitionCommand, BusinessObjectDefinitionDetailDto>
{
    public async Task<Result<BusinessObjectDefinitionDetailDto>> Handle(
        SaveUnpublishedBusinessObjectDefinitionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return BusinessObjectDefinitionFailures.MissingWorkspace<BusinessObjectDefinitionDetailDto>();

        BusinessObjectDefinitionId definitionId = BusinessObjectDefinitionId.From(command.BusinessObjectDefinitionId);
        BusinessObjectDefinition? definition =
            await repository.GetByIdForWorkspaceAsync(definitionId, workspaceId, cancellationToken);
        if (definition is null)
            return BusinessObjectDefinitionFailures.NotFound<BusinessObjectDefinitionDetailDto>();

        Result<IReadOnlyList<BusinessObjectFieldDefinitionSpec>> fields = await inputPlanner.PlanAsync(
            workspaceId,
            command.Fields,
            cancellationToken);
        if (fields.IsFailure)
            return BusinessObjectDefinitionFailures.Invalid<BusinessObjectDefinitionDetailDto>(fields.Error);

        Result saved = definition.SaveUnpublished(
            command.Name,
            fields.Value,
            command.ExpectedRevision,
            DateTime.UtcNow);
        if (saved.IsFailure)
            return MapDomainFailure(saved);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            return BusinessObjectDefinitionFailures.Conflict<BusinessObjectDefinitionDetailDto>(
                "The object definition has changed.");
        }
        catch (UniqueConstraintException)
        {
            return BusinessObjectDefinitionFailures.DuplicateObjectKey<BusinessObjectDefinitionDetailDto>();
        }

        return BusinessObjectDefinitionMapper.ToDetailDto(definition);
    }

    private static Result<BusinessObjectDefinitionDetailDto> MapDomainFailure(Result result) =>
        result.ErrorCode == ErrorCodes.Conflict
            ? BusinessObjectDefinitionFailures.Conflict<BusinessObjectDefinitionDetailDto>(result.Error)
            : BusinessObjectDefinitionFailures.Invalid<BusinessObjectDefinitionDetailDto>(result.Error);
}
