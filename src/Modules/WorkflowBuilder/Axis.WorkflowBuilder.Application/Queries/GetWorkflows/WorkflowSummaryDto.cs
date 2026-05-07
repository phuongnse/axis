using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.WorkflowBuilder.Application.Queries.GetWorkflows;

public sealed record WorkflowSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    WorkflowStatus Status,
    int StepCount,
    int TriggerCount,
    DateTime UpdatedAt);
