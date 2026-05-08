using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetDataClasses;

/// <summary>US-037: Returns all data classes for an organization.</summary>
public sealed record GetDataClassesQuery(Guid OrganizationId) : IQuery<IReadOnlyList<DataClassSummaryDto>>;
