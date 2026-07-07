using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Application.Queries.ListObjectDefinitions;

public sealed record ListObjectDefinitionsQuery(
    int Page,
    int PageSize)
    : IQuery<Result<PagedResult<ObjectDefinitionListItemDto>>>;
