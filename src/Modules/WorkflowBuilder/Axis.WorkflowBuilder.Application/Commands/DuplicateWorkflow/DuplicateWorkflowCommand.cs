using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.DuplicateWorkflow;

public sealed record DuplicateWorkflowCommand(
    Guid WorkflowId,
    Guid OrganizationId,
    string CreatedBy) : ICommand<Guid>;
