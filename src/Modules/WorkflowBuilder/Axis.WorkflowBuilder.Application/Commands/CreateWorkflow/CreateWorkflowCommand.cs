using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.CreateWorkflow;

/// <summary>Create a new workflow definition in Draft status.</summary>
public sealed record CreateWorkflowCommand(
    string Name,
    string? Description,
    Guid TeamAccountId,
    string CreatedBy) : ICommand<Guid>;
