using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Queries.GetBusinessObjectRecord;

public sealed record GetBusinessObjectRecordQuery(Guid RecordId, string? CorrelationId = null)
    : IQuery<Result<BusinessObjectRecordDetailDto>>;
