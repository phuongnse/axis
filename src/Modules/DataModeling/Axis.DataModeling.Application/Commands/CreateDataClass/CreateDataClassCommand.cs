using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.CreateDataClass;

/// <summary>Create a reusable data class definition.</summary>
public sealed record CreateDataClassCommand(
    string Name,
    string? Description,
    Guid tenantId,
    string CreatedBy) : ICommand<Guid>;
