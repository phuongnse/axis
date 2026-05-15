namespace Axis.WorkflowBuilder.Application.Queries.ExportWorkflow;

public sealed record TransitionExportDto(
    Guid FromStepId,
    Guid ToStepId,
    string? Label);
