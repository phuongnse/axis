using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Queries.ListBusinessObjectDefinitions;

public sealed class ListBusinessObjectDefinitionsHandler(
    ICurrentUser currentUser,
    IBusinessObjectDefinitionRepository repository)
    : IQueryHandler<ListBusinessObjectDefinitionsQuery, Result<PagedResult<BusinessObjectDefinitionListItemDto>>>
{
    public async Task<Result<PagedResult<BusinessObjectDefinitionListItemDto>>> Handle(
        ListBusinessObjectDefinitionsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return BusinessObjectDefinitionFailures.MissingWorkspace<PagedResult<BusinessObjectDefinitionListItemDto>>();

        int totalCount = await repository.CountForWorkspaceAsync(workspaceId, cancellationToken);
        IReadOnlyList<BusinessObjectDefinition> definitions =
            await repository.ListForWorkspaceAsync(
                workspaceId,
                query.Page,
                query.PageSize,
                cancellationToken);

        return new PagedResult<BusinessObjectDefinitionListItemDto>(
            definitions.Select(BusinessObjectDefinitionMapper.ToListItemDto).ToList(),
            totalCount,
            query.Page,
            query.PageSize);
    }
}
