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
    IWorkspaceProductBuilderAuthorization productBuilderAuthorization,
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

        WorkspaceProductBuilderDecision builderDecision = await BusinessObjectAuthorization.AuthorizeBuilderAsync(
            productBuilderAuthorization,
            workspaceId,
            currentSubject.Subject,
            cancellationToken);
        bool publishedOnly = !builderDecision.IsAllowed;
        if (builderDecision.IsUnavailable)
            return BusinessObjectDefinitionFailures.Authorization<PagedResult<BusinessObjectDefinitionListItemDto>>(builderDecision);
        if (publishedOnly)
        {
            ProductAuthorizationDecision publishedReadDecision = await BusinessObjectAuthorization.AuthorizeAsync(
                authorization,
                workspaceId,
                currentSubject.Subject,
                BusinessObjectProductActions.DefinitionReadPublished,
                BusinessObjectProductActions.DefinitionResourceType,
                null,
                query.CorrelationId,
                cancellationToken);
            if (!publishedReadDecision.IsAllowed)
                return BusinessObjectDefinitionFailures.Authorization<PagedResult<BusinessObjectDefinitionListItemDto>>(publishedReadDecision);
        }

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
