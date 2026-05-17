namespace Axis.WorkflowEngine.Application.DTOs;

public sealed record ExecutionSummaryResponse(
    Guid Id,
    Guid WorkflowDefinitionId,
    string Status,
    string TriggerType,
    Guid? TriggeredByUserId,
    Guid? RetryOfExecutionId,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);
