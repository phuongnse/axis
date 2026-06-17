using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.UpdateModel;

/// <summary>Update a model's name, description, icon, and color.</summary>
public sealed record UpdateModelCommand(
    Guid ModelId,
    Guid workspaceId,
    string Name,
    string? Description,
    string? Icon,
    string? Color) : ICommand;
