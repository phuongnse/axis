using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.WorkflowBuilder.Application.Queries.ExportWorkflow;

public sealed record TriggerExportDto(
    TriggerType Type,
    IReadOnlyDictionary<string, object?>? Config);
