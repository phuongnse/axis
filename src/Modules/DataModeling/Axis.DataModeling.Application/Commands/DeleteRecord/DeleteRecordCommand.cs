using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.DeleteRecord;

/// <summary>US-045: Soft-delete a record.</summary>
public sealed record DeleteRecordCommand(
    Guid RecordId,
    Guid ModelId,
    Guid OrganizationId) : ICommand;
