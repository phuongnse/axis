using Axis.Objects.Application.Repositories;
using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Application.Queries.GetObjectDefinition;

public sealed class GetObjectDefinitionHandler(
    ICurrentUser currentUser,
    IObjectDefinitionRepository repository)
    : IQueryHandler<GetObjectDefinitionQuery, Result<ObjectDefinitionDetailDto>>
{
    public async Task<Result<ObjectDefinitionDetailDto>> Handle(
        GetObjectDefinitionQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return ObjectDefinitionFailures.MissingWorkspace<ObjectDefinitionDetailDto>();

        ObjectDefinition? definition = await repository.GetByIdForWorkspaceAsync(
            ObjectDefinitionId.From(query.ObjectDefinitionId),
            workspaceId,
            cancellationToken);

        return definition is null
            ? ObjectDefinitionFailures.NotFound<ObjectDefinitionDetailDto>()
            : ObjectDefinitionMapper.ToDetailDto(definition);
    }
}
