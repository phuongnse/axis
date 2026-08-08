using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Queries.ListBusinessObjectDefinitions;

public sealed record ListBusinessObjectDefinitionsQuery(
    int Page,
    int PageSize,
    string? SearchQuery = null,
    string? Language = null,
    string? CorrelationId = null)
    : IQuery<Result<PagedResult<BusinessObjectDefinitionListItemDto>>>;
