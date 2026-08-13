using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Identity.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Queries.GetBusinessObjectDefinition;

public sealed class GetBusinessObjectDefinitionHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IWorkspaceProductBuilderAuthorization productBuilderAuthorization,
    IProductAuthorizationService authorization,
    IBusinessObjectDefinitionRepository repository)
    : IQueryHandler<GetBusinessObjectDefinitionQuery, Result<BusinessObjectDefinitionDetailDto>>
{
    public async Task<Result<BusinessObjectDefinitionDetailDto>> Handle(
        GetBusinessObjectDefinitionQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return BusinessObjectDefinitionFailures.MissingWorkspace<BusinessObjectDefinitionDetailDto>();
        if (currentSubject.Subject.Id == Guid.Empty || !Enum.IsDefined(currentSubject.Subject.Kind))
            return BusinessObjectDefinitionFailures.MissingUser<BusinessObjectDefinitionDetailDto>();

        BusinessObjectDefinition? definition = await repository.GetByIdForWorkspaceAsync(
            BusinessObjectDefinitionId.From(query.BusinessObjectDefinitionId),
            workspaceId,
            cancellationToken);

        if (definition is null)
            return BusinessObjectDefinitionFailures.NotFound<BusinessObjectDefinitionDetailDto>();

        WorkspaceProductBuilderDecision builderDecision = await BusinessObjectAuthorization.AuthorizeBuilderAsync(
            productBuilderAuthorization,
            workspaceId,
            currentSubject.Subject,
            cancellationToken);
        if (builderDecision.IsUnavailable)
            return BusinessObjectDefinitionFailures.Authorization<BusinessObjectDefinitionDetailDto>(builderDecision);
        if (builderDecision.IsAllowed)
            return BusinessObjectDefinitionMapper.ToDetailDto(definition, canManage: true);
        if (definition.Status != BusinessObjectDefinitionStatus.Published)
            return BusinessObjectDefinitionFailures.Forbidden<BusinessObjectDefinitionDetailDto>();

        ProductAuthorizationDecision publishedReadDecision = await BusinessObjectAuthorization.AuthorizeAsync(
            authorization,
            workspaceId,
            currentSubject.Subject,
            BusinessObjectProductActions.DefinitionReadPublished,
            BusinessObjectProductActions.DefinitionResourceType,
            definition.Key.Value,
            query.CorrelationId,
            cancellationToken);
        if (!publishedReadDecision.IsAllowed)
            return BusinessObjectDefinitionFailures.Authorization<BusinessObjectDefinitionDetailDto>(publishedReadDecision);

        return BusinessObjectDefinitionMapper.ToDetailDto(definition, canManage: false);
    }
}
