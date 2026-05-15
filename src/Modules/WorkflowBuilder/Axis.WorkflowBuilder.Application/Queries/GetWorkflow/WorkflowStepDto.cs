using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.WorkflowBuilder.Application.Queries.GetWorkflow;

public sealed record WorkflowStepDto(
    Guid Id,
    string Name,
    StepType Type,
    IReadOnlyDictionary<string, object?>? Config);
