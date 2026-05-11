using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.CreateWorkflow;

/// <summary>US-047: Create a new workflow definition in Draft status.</summary>
public sealed record CreateWorkflowCommand(
    string Name,
    string? Description,
    Guid OrganizationId,
    string CreatedBy) : ICommand<Guid>;
