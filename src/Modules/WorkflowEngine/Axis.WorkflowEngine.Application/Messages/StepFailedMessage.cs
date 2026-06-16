namespace Axis.WorkflowEngine.Application.Messages;

/// <summary>
/// Dispatched by a step handler when the step fails (exception, timeout, bad response).
/// Processed by StepFailedHandler, which marks the step and execution as failed.
/// </summary>
public sealed record StepFailedMessage(
    Guid ExecutionId,
    Guid StepId,
    Guid OrganizationId,
    string ErrorDetails);
