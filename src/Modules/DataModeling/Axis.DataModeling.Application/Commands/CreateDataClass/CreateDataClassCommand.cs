using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.CreateDataClass;

/// <summary>US-037: Create a reusable data class definition.</summary>
public sealed record CreateDataClassCommand(
    string Name,
    string? Description,
    Guid OrganizationId,
    string CreatedBy) : ICommand<Guid>;
