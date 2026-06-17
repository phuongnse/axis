using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.RemoveFieldFromDataClass;

/// <summary>Remove a field from a data class.</summary>
public sealed record RemoveFieldFromDataClassCommand(
    Guid DataClassId,
    Guid FieldId,
    Guid workspaceId) : ICommand;
