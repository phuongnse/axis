using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.DuplicateWorkflow;

public sealed record DuplicateWorkflowCommand(
    Guid WorkflowId,
    Guid TeamAccountId,
    string CreatedBy) : ICommand<Guid>;
