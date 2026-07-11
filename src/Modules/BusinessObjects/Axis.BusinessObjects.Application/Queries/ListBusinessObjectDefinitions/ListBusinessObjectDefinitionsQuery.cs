using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Queries.ListBusinessObjectDefinitions;

public sealed record ListBusinessObjectDefinitionsQuery(
    int Page,
    int PageSize)
    : IQuery<Result<PagedResult<BusinessObjectDefinitionListItemDto>>>;
