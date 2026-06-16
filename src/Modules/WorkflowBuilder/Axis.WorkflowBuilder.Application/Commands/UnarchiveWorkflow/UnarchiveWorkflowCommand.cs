using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.UnarchiveWorkflow;

public sealed record UnarchiveWorkflowCommand(Guid WorkflowId, Guid OrganizationId) : ICommand;
