using Axis.DataModeling.Application.Queries.GetRecords;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Queries.GetRecord;

/// <summary>Returns a single record by ID.</summary>
public sealed record GetRecordQuery(
    Guid RecordId,
    Guid ModelId,
    Guid TeamAccountId) : IQuery<Result<RecordDto>>;
