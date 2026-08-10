using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Identity.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Queries.ListBusinessObjectDefinitions;

public sealed class ListBusinessObjectDefinitionsHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IProductAuthorizationService authorization,
    IBusinessObjectDefinitionRepository repository)
    : IQueryHandler<ListBusinessObjectDefinitionsQuery, Result<PagedResult<BusinessObjectDefinitionListItemDto>>>
{
    public async Task<Result<PagedResult<BusinessObjectDefinitionListItemDto>>> Handle(
        ListBusinessObjectDefinitionsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return BusinessObjectDefinitionFailures.MissingWorkspace<PagedResult<BusinessObjectDefinitionListItemDto>>();
        if (currentSubject.Subject.Id == Guid.Empty || !Enum.IsDefined(currentSubject.Subject.Kind))
            return BusinessObjectDefinitionFailures.MissingUser<PagedResult<BusinessObjectDefinitionListItemDto>>();

        ProductAuthorizationDecision decision = await BusinessObjectAuthorization.AuthorizeAsync(
            authorization,
            workspaceId,
            currentSubject.Subject,
            BusinessObjectProductActions.DefinitionRead,
            BusinessObjectProductActions.DefinitionResourceType,
            null,
            query.CorrelationId,
            cancellationToken);
        bool publishedOnly = !decision.IsAllowed;
        if (decision.IsUnavailable)
            return BusinessObjectDefinitionFailures.Authorization<PagedResult<BusinessObjectDefinitionListItemDto>>(decision);
        if (publishedOnly)
        {
            decision = await BusinessObjectAuthorization.AuthorizeAsync(
                authorization,
                workspaceId,
                currentSubject.Subject,
                BusinessObjectProductActions.DefinitionReadPublished,
                BusinessObjectProductActions.DefinitionResourceType,
                null,
                query.CorrelationId,
                cancellationToken);
        }
        if (!decision.IsAllowed)
            return BusinessObjectDefinitionFailures.Authorization<PagedResult<BusinessObjectDefinitionListItemDto>>(decision);

        int totalCount = await repository.CountForWorkspaceAsync(
            workspaceId,
            query.SearchQuery,
            publishedOnly,
            cancellationToken);
        IReadOnlyList<BusinessObjectDefinition> definitions =
            await repository.ListForWorkspaceAsync(
                workspaceId,
                query.Page,
                query.PageSize,
                query.SearchQuery,
                publishedOnly,
                cancellationToken);

        return new PagedResult<BusinessObjectDefinitionListItemDto>(
            definitions.Select(BusinessObjectDefinitionMapper.ToListItemDto).ToList(),
            totalCount,
            query.Page,
            query.PageSize);
    }
}
