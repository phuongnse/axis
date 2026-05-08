using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetModel;

/// <summary>US-031/032: Returns a single model with its full field list.</summary>
public sealed record GetModelQuery(Guid ModelId, Guid OrganizationId) : IQuery<ModelDetailDto?>;
