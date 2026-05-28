using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.PublishWorkflow;

/// <summary>Validate and publish a workflow definition to Active status.</summary>
public sealed record PublishWorkflowCommand(Guid WorkflowId, Guid OrganizationId) : ICommand;
