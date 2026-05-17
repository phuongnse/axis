namespace Axis.WorkflowEngine.Application.DTOs;

public sealed record ExecutionResponse(
    Guid Id,
    Guid WorkflowDefinitionId,
    string Status,
    string TriggerType,
    Guid? TriggeredByUserId,
    Guid? RetryOfExecutionId,
    string? ErrorMessage,
    IReadOnlyDictionary<string, object?> Context,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    IReadOnlyList<ExecutionStepResponse> Steps);
