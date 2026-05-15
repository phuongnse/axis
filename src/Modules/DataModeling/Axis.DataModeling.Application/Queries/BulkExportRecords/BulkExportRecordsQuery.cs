using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Queries.BulkExportRecords;

public sealed record BulkExportRecordsQuery(
    Guid ModelId,
    Guid OrganizationId,
    IReadOnlyList<Guid> RecordIds) : IQuery<Result<string>>;
