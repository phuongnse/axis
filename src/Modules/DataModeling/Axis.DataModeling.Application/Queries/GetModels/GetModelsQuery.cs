using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetModels;

/// <summary>US-031: Returns a paginated list of models for an organization.</summary>
public sealed record GetModelsQuery(Guid OrganizationId, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<ModelSummaryDto>>;
