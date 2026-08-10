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

        ProductAuthorizationDecision decision = await BusinessObjectAuthorization.AuthorizeAsync(
            authorization,
            workspaceId,
            currentSubject.Subject,
            BusinessObjectProductActions.DefinitionRead,
            BusinessObjectProductActions.DefinitionResourceType,
            definition.Key.Value,
            query.CorrelationId,
            cancellationToken);
        if (decision.IsUnavailable)
            return BusinessObjectDefinitionFailures.Authorization<BusinessObjectDefinitionDetailDto>(decision);
        if (!decision.IsAllowed && definition.Status == BusinessObjectDefinitionStatus.Published)
        {
            decision = await BusinessObjectAuthorization.AuthorizeAsync(
                authorization,
                workspaceId,
                currentSubject.Subject,
                BusinessObjectProductActions.DefinitionReadPublished,
                BusinessObjectProductActions.DefinitionResourceType,
                definition.Key.Value,
                query.CorrelationId,
                cancellationToken);
        }

        if (decision.IsUnavailable)
            return BusinessObjectDefinitionFailures.Authorization<BusinessObjectDefinitionDetailDto>(decision);

        if (!decision.IsAllowed)
            return BusinessObjectDefinitionFailures.Forbidden<BusinessObjectDefinitionDetailDto>();

        ProductAuthorizationDecision manageDecision = await BusinessObjectAuthorization.AuthorizeAsync(
            authorization,
            workspaceId,
            currentSubject.Subject,
            BusinessObjectProductActions.DefinitionManage,
            BusinessObjectProductActions.DefinitionResourceType,
            definition.Key.Value,
            query.CorrelationId,
            cancellationToken);
        if (manageDecision.IsUnavailable)
            return BusinessObjectDefinitionFailures.Authorization<BusinessObjectDefinitionDetailDto>(manageDecision);

        return BusinessObjectDefinitionMapper.ToDetailDto(definition, manageDecision.IsAllowed);
    }
}
