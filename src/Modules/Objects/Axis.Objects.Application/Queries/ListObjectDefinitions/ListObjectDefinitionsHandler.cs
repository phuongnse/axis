using Axis.Objects.Application.Repositories;
using Axis.Objects.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Application.Queries.ListObjectDefinitions;

public sealed class ListObjectDefinitionsHandler(
    ICurrentUser currentUser,
    IObjectDefinitionRepository repository)
    : IQueryHandler<ListObjectDefinitionsQuery, Result<PagedResult<ObjectDefinitionListItemDto>>>
{
    public async Task<Result<PagedResult<ObjectDefinitionListItemDto>>> Handle(
        ListObjectDefinitionsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return ObjectDefinitionFailures.MissingWorkspace<PagedResult<ObjectDefinitionListItemDto>>();

        int totalCount = await repository.CountForWorkspaceAsync(workspaceId, cancellationToken);
        IReadOnlyList<ObjectDefinition> definitions =
            await repository.ListForWorkspaceAsync(
                workspaceId,
                query.Page,
                query.PageSize,
                cancellationToken);

        return new PagedResult<ObjectDefinitionListItemDto>(
            definitions.Select(ObjectDefinitionMapper.ToListItemDto).ToList(),
            totalCount,
            query.Page,
            query.PageSize);
    }
}
