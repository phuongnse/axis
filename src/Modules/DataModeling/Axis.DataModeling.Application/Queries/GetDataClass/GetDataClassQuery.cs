using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetDataClass;

/// <summary>US-039: Returns a single data class with its full field list.</summary>
public sealed record GetDataClassQuery(Guid DataClassId, Guid OrganizationId) : IQuery<DataClassDetailDto?>;
