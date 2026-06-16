using Axis.Shared.Application.CQRS;
using Axis.WorkflowBuilder.Application.Queries.ExportWorkflow;

namespace Axis.WorkflowBuilder.Application.Commands.ImportWorkflow;

public sealed record ImportWorkflowCommand(
    Guid tenantId,
    string CreatedBy,
    WorkflowExportDto ExportData) : ICommand<Guid>;
