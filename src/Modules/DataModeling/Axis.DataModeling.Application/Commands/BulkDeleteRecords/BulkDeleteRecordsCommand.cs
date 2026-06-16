using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.BulkDeleteRecords;

/// <summary>Soft-deletes multiple records in a single operation.</summary>
public sealed record BulkDeleteRecordsCommand(
    IReadOnlyList<Guid> RecordIds,
    Guid ModelId,
    Guid OrganizationId) : ICommand<BulkDeleteResult>;
