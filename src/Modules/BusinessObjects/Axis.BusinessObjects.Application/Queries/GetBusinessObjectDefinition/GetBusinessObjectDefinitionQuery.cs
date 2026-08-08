using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Queries.GetBusinessObjectDefinition;

public sealed record GetBusinessObjectDefinitionQuery(Guid BusinessObjectDefinitionId, string? CorrelationId = null)
    : IQuery<Result<BusinessObjectDefinitionDetailDto>>;
