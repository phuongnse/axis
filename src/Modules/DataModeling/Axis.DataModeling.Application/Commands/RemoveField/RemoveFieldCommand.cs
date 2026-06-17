using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.RemoveField;

/// <summary>/034: Remove a custom field from a model.</summary>
public sealed record RemoveFieldCommand(
    Guid ModelId,
    Guid FieldId,
    Guid workspaceId) : ICommand;
