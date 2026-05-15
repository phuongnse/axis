namespace Axis.WorkflowBuilder.Application.Queries.ExportWorkflow;

public sealed record WorkflowExportDto(
    string Name,
    string? Description,
    IReadOnlyList<StepExportDto> Steps,
    IReadOnlyList<TransitionExportDto> Transitions,
    IReadOnlyList<TriggerExportDto> Triggers);
