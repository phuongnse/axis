using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetModels;

/// <summary>US-031: Returns all models for an organization.</summary>
public sealed record GetModelsQuery(Guid OrganizationId) : IQuery<IReadOnlyList<ModelSummaryDto>>;
