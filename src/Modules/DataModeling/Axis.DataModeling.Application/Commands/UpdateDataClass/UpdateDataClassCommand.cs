using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.UpdateDataClass;

/// <summary>US-039: Update a data class's name and description.</summary>
public sealed record UpdateDataClassCommand(
    Guid DataClassId,
    Guid OrganizationId,
    string Name,
    string? Description) : ICommand;
