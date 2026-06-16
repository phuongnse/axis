using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.CreateModel;

/// <summary>Create a new data model with system fields.</summary>
public sealed record CreateModelCommand(
    string Name,
    string? Description,
    string? Icon,
    string? Color,
    Guid workspaceId,
    string CreatedBy) : ICommand<Guid>;
