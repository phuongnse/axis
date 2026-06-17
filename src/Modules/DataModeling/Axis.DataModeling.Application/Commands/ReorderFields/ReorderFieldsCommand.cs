using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.ReorderFields;

/// <summary>Reorder custom fields within a model by providing the desired field ID sequence.</summary>
public sealed record ReorderFieldsCommand(
    Guid ModelId,
    Guid workspaceId,
    IReadOnlyList<Guid> OrderedFieldIds) : ICommand;
