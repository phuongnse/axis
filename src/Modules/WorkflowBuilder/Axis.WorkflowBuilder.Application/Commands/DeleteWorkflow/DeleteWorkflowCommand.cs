using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.DeleteWorkflow;

public sealed record DeleteWorkflowCommand(Guid WorkflowId, Guid TeamAccountId) : ICommand;
