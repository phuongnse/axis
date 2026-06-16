using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.UpdateDataClass;

/// <summary>Update a data class's name and description.</summary>
public sealed record UpdateDataClassCommand(
    Guid DataClassId,
    Guid tenantId,
    string Name,
    string? Description) : ICommand;
