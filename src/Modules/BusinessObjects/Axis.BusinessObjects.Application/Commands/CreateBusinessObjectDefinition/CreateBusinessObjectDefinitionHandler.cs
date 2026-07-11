using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Commands.CreateBusinessObjectDefinition;

public sealed class CreateBusinessObjectDefinitionHandler(
    ICurrentUser currentUser,
    IBusinessObjectDefinitionRepository repository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateBusinessObjectDefinitionCommand, BusinessObjectDefinitionDetailDto>
{
    public async Task<Result<BusinessObjectDefinitionDetailDto>> Handle(
        CreateBusinessObjectDefinitionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return BusinessObjectDefinitionFailures.MissingWorkspace<BusinessObjectDefinitionDetailDto>();

        Result<BusinessObjectDefinitionKey> key = BusinessObjectDefinitionKey.CreateFromName(command.Name);
        if (key.IsFailure)
            return BusinessObjectDefinitionFailures.Invalid<BusinessObjectDefinitionDetailDto>(key.Error);

        if (await repository.ObjectKeyExistsAsync(workspaceId, key.Value, ct: cancellationToken))
            return BusinessObjectDefinitionFailures.DuplicateObjectKey<BusinessObjectDefinitionDetailDto>();

        Result<BusinessObjectDefinition> definition = BusinessObjectDefinition.CreateUnpublished(
            workspaceId,
            command.Name,
            key.Value,
            DateTime.UtcNow);
        if (definition.IsFailure)
            return BusinessObjectDefinitionFailures.Invalid<BusinessObjectDefinitionDetailDto>(definition.Error);

        await repository.AddAsync(definition.Value, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            return BusinessObjectDefinitionFailures.DuplicateObjectKey<BusinessObjectDefinitionDetailDto>();
        }

        return BusinessObjectDefinitionMapper.ToDetailDto(definition.Value);
    }
}
