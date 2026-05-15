using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.WorkflowBuilder.Application.Queries.GetWorkflow;

public sealed record WorkflowTriggerDto(
    TriggerType Type,
    IReadOnlyDictionary<string, object?>? Config);
