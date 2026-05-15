using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.WorkflowBuilder.Application.Queries.GetWorkflow;

public sealed record WorkflowDetailDto(
    Guid Id,
    string Name,
    string? Description,
    WorkflowStatus Status,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<WorkflowStepDto> Steps,
    IReadOnlyList<StepTransitionDto> Transitions,
    IReadOnlyList<WorkflowTriggerDto> Triggers);
