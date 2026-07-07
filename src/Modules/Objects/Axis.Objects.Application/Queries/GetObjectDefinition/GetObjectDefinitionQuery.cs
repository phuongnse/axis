using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Application.Queries.GetObjectDefinition;

public sealed record GetObjectDefinitionQuery(Guid ObjectDefinitionId)
    : IQuery<Result<ObjectDefinitionDetailDto>>;
