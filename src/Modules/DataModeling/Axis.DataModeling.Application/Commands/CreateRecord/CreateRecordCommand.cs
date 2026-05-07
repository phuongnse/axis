using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.CreateRecord;

/// <summary>US-041: Create a new record for a model.</summary>
public sealed record CreateRecordCommand(
    Guid ModelId,
    Guid OrganizationId,
    IReadOnlyDictionary<string, object?> Data) : ICommand<Guid>;
