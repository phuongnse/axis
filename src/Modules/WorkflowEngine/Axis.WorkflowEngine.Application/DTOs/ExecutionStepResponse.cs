namespace Axis.WorkflowEngine.Application.DTOs;

public sealed record ExecutionStepResponse(
    Guid Id,
    Guid StepDefinitionId,
    string Name,
    string StepType,
    int DisplayOrder,
    string Status,
    IReadOnlyDictionary<string, object?>? InputSnapshot,
    IReadOnlyDictionary<string, object?>? OutputSnapshot,
    string? ErrorDetails,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt);
