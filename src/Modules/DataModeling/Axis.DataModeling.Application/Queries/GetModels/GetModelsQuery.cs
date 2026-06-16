using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetModels;

/// <summary>Returns a paginated list of models for a tenant.</summary>
public sealed record GetModelsQuery(Guid tenantId, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<ModelSummaryDto>>;
