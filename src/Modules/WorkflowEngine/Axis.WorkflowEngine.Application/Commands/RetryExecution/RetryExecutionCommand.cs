using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowEngine.Application.Commands.RetryExecution;

/// <summary>US-100: Create a new retry execution for a failed execution.</summary>
public sealed record RetryExecutionCommand(
    Guid ExecutionId,
    Guid OrganizationId,
    Guid? RetriedByUserId) : ICommand<Guid>;
