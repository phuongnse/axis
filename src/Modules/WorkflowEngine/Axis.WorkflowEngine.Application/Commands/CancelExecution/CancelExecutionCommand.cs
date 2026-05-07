using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowEngine.Application.Commands.CancelExecution;

/// <summary>US-092: Cancel a running or pending execution.</summary>
public sealed record CancelExecutionCommand(Guid ExecutionId, Guid OrganizationId) : ICommand;
