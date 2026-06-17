using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.DeleteDataClass;

/// <summary>Soft-delete a data class that is not referenced by any model field.</summary>
public sealed record DeleteDataClassCommand(Guid DataClassId, Guid workspaceId) : ICommand;
