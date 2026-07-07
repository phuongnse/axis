using Axis.Objects.Application.Repositories;
using Axis.Objects.Application.Services;
using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Application.Commands.CreateObjectDefinitionDraft;

public sealed class CreateObjectDefinitionDraftHandler(
    ICurrentUser currentUser,
    IObjectDefinitionRepository repository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateObjectDefinitionDraftCommand, ObjectDefinitionDetailDto>
{
    public async Task<Result<ObjectDefinitionDetailDto>> Handle(
        CreateObjectDefinitionDraftCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return ObjectDefinitionFailures.MissingWorkspace<ObjectDefinitionDetailDto>();

        Result<ObjectDefinitionKey> key = ObjectDefinitionKey.CreateFromName(command.Name);
        if (key.IsFailure)
            return ObjectDefinitionFailures.Invalid<ObjectDefinitionDetailDto>(key.Error);

        if (await repository.ObjectKeyExistsAsync(workspaceId, key.Value, ct: cancellationToken))
            return ObjectDefinitionFailures.DuplicateObjectKey<ObjectDefinitionDetailDto>();

        Result<ObjectDefinition> draft = ObjectDefinition.CreateDraft(
            workspaceId,
            command.Name,
            key.Value,
            DateTime.UtcNow);
        if (draft.IsFailure)
            return ObjectDefinitionFailures.Invalid<ObjectDefinitionDetailDto>(draft.Error);

        await repository.AddAsync(draft.Value, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            return ObjectDefinitionFailures.DuplicateObjectKey<ObjectDefinitionDetailDto>();
        }

        return ObjectDefinitionMapper.ToDetailDto(draft.Value);
    }
}
