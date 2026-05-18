using Axis.DataModeling.Application.Queries.GetRecords;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Queries.ExportRecordsCsv;

/// <summary>US-046: Exports all matching records as a CSV file (streamed, no size limit for sync export).</summary>
public sealed record ExportRecordsCsvQuery(
    Guid ModelId,
    Guid OrganizationId,
    string? Search = null,
    IReadOnlyList<RecordFilter>? Filters = null,
    string? SortBy = null,
    string? SortDir = null) : IQuery<Result<CsvExportDto>>;
