using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Queries.GetBusinessObjectDefinition;

public sealed class GetBusinessObjectDefinitionHandler(
    ICurrentUser currentUser,
    IBusinessObjectDefinitionRepository repository)
    : IQueryHandler<GetBusinessObjectDefinitionQuery, Result<BusinessObjectDefinitionDetailDto>>
{
    public async Task<Result<BusinessObjectDefinitionDetailDto>> Handle(
        GetBusinessObjectDefinitionQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return BusinessObjectDefinitionFailures.MissingWorkspace<BusinessObjectDefinitionDetailDto>();

        BusinessObjectDefinition? definition = await repository.GetByIdForWorkspaceAsync(
            BusinessObjectDefinitionId.From(query.BusinessObjectDefinitionId),
            workspaceId,
            cancellationToken);

        return definition is null
            ? BusinessObjectDefinitionFailures.NotFound<BusinessObjectDefinitionDetailDto>()
            : BusinessObjectDefinitionMapper.ToDetailDto(definition);
    }
}
