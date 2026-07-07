using Axis.Objects.Application.Repositories;
using Axis.Objects.Application.Services;
using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Application.Commands.PublishObjectDefinition;

public sealed class PublishObjectDefinitionHandler(
    ICurrentUser currentUser,
    IObjectDefinitionRepository repository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<PublishObjectDefinitionCommand, ObjectDefinitionDetailDto>
{
    public async Task<Result<ObjectDefinitionDetailDto>> Handle(
        PublishObjectDefinitionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return ObjectDefinitionFailures.MissingWorkspace<ObjectDefinitionDetailDto>();

        if (currentUser.UserId is not Guid userId)
            return ObjectDefinitionFailures.MissingUser<ObjectDefinitionDetailDto>();

        ObjectDefinition? definition = await repository.GetByIdForWorkspaceAsync(
            ObjectDefinitionId.From(command.ObjectDefinitionId),
            workspaceId,
            cancellationToken);
        if (definition is null)
            return ObjectDefinitionFailures.NotFound<ObjectDefinitionDetailDto>();

        Result<ObjectDefinitionVersion> published = definition.Publish(
            command.ExpectedDraftVersion,
            userId,
            DateTime.UtcNow);
        if (published.IsFailure)
            return MapDomainFailure(published);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            return ObjectDefinitionFailures.Conflict<ObjectDefinitionDetailDto>(
                "The object definition draft has changed.");
        }

        return ObjectDefinitionMapper.ToDetailDto(definition);
    }

    private static Result<ObjectDefinitionDetailDto> MapDomainFailure(Result result) =>
        result.ErrorCode == ErrorCodes.Conflict
            ? ObjectDefinitionFailures.Conflict<ObjectDefinitionDetailDto>(result.Error)
            : ObjectDefinitionFailures.Invalid<ObjectDefinitionDetailDto>(result.Error);
}
