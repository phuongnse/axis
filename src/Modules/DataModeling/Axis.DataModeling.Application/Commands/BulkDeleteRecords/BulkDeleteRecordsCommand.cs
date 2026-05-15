using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.BulkDeleteRecords;

public sealed record BulkDeleteRecordsCommand(
    Guid ModelId,
    Guid OrganizationId,
    IReadOnlyList<Guid> RecordIds) : ICommand;
