using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.CreateRecord;

/// <summary>Create a new record for a model.</summary>
public sealed record CreateRecordCommand(
    Guid ModelId,
    Guid workspaceId,
    IReadOnlyDictionary<string, object?> Data,
    string CreatedBy) : ICommand<Guid>;
