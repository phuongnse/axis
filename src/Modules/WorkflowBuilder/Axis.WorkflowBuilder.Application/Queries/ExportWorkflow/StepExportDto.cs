using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.WorkflowBuilder.Application.Queries.ExportWorkflow;

public sealed record StepExportDto(
    Guid Id,
    string Name,
    StepType Type,
    IReadOnlyDictionary<string, object?>? Config);
