using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Queries.ListBusinessObjectRecords;

public sealed record ListBusinessObjectRecordsQuery(
    int Page,
    int PageSize,
    string? ObjectKey = null,
    string? CorrelationId = null)
    : IQuery<Result<PagedResult<BusinessObjectRecordListItemDto>>>;
