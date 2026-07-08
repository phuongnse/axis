using Axis.Objects.Application.Repositories;
using Axis.Objects.Application.Services;
using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Application.Commands.SaveUnpublishedObjectDefinition;

public sealed class SaveUnpublishedObjectDefinitionHandler(
    ICurrentUser currentUser,
    IObjectDefinitionRepository repository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SaveUnpublishedObjectDefinitionCommand, ObjectDefinitionDetailDto>
{
    public async Task<Result<ObjectDefinitionDetailDto>> Handle(
        SaveUnpublishedObjectDefinitionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return ObjectDefinitionFailures.MissingWorkspace<ObjectDefinitionDetailDto>();

        ObjectDefinitionId definitionId = ObjectDefinitionId.From(command.ObjectDefinitionId);
        ObjectDefinition? definition =
            await repository.GetByIdForWorkspaceAsync(definitionId, workspaceId, cancellationToken);
        if (definition is null)
            return ObjectDefinitionFailures.NotFound<ObjectDefinitionDetailDto>();

        Result saved = definition.SaveUnpublished(
            command.Name,
            ObjectDefinitionMapper.ToDomainSpecs(command.Fields),
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
            return ObjectDefinitionFailures.Conflict<ObjectDefinitionDetailDto>(
                "The object definition has changed.");
        }
        catch (UniqueConstraintException)
        {
            return ObjectDefinitionFailures.DuplicateObjectKey<ObjectDefinitionDetailDto>();
        }

        return ObjectDefinitionMapper.ToDetailDto(definition);
    }

    private static Result<ObjectDefinitionDetailDto> MapDomainFailure(Result result) =>
        result.ErrorCode == ErrorCodes.Conflict
            ? ObjectDefinitionFailures.Conflict<ObjectDefinitionDetailDto>(result.Error)
            : ObjectDefinitionFailures.Invalid<ObjectDefinitionDetailDto>(result.Error);
}
