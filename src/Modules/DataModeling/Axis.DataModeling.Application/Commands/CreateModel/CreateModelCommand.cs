using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.CreateModel;

/// <summary>US-030: Create a new custom data model within an organization.</summary>
public sealed record CreateModelCommand(
    string Name,
    string? Description,
    string? Icon,
    string? Color,
    Guid OrganizationId) : ICommand<Guid>;
