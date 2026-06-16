using Axis.Shared.Application.CQRS;
using Axis.WorkflowBuilder.Application.Queries.ExportWorkflow;

namespace Axis.WorkflowBuilder.Application.Commands.ImportWorkflow;

public sealed record ImportWorkflowCommand(
    Guid TeamAccountId,
    string CreatedBy,
    WorkflowExportDto ExportData) : ICommand<Guid>;
