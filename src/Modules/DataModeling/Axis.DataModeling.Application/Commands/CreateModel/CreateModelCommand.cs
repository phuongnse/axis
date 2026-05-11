using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.CreateModel;

/// <summary>US-030: Create a new data model with system fields.</summary>
public sealed record CreateModelCommand(
    string Name,
    string? Description,
    string? Icon,
    string? Color,
    Guid OrganizationId,
    string CreatedBy) : ICommand<Guid>;
