using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.UpdateModel;

/// <summary>US-032: Update a model's name, description, icon, and color.</summary>
public sealed record UpdateModelCommand(
    Guid ModelId,
    Guid OrganizationId,
    string Name,
    string? Description,
    string? Icon,
    string? Color) : ICommand;
