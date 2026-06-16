using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.UpdateRecord;

/// <summary>Replace the data of an existing record (full replace — partial update handled at API layer).</summary>
public sealed record UpdateRecordCommand(
    Guid RecordId,
    Guid ModelId,
    Guid tenantId,
    IReadOnlyDictionary<string, object?> Data) : ICommand;
