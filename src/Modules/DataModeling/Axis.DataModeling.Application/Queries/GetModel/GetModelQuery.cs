using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Queries.GetModel;

/// <summary>/032: Returns a single model with its full field list.</summary>
public sealed record GetModelQuery(Guid ModelId, Guid OrganizationId) : IQuery<Result<ModelDetailDto>>;
