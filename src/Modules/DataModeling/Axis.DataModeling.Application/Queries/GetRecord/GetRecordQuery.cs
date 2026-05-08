using Axis.DataModeling.Application.Queries.GetRecords;
using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetRecord;

/// <summary>US-044: Returns a single record by ID.</summary>
public sealed record GetRecordQuery(
    Guid RecordId,
    Guid ModelId,
    Guid OrganizationId) : IQuery<RecordDto?>;
