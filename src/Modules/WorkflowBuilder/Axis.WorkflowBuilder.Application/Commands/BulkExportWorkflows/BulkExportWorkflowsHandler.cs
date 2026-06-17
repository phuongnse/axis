using Axis.Shared.Application.CQRS;
using Axis.WorkflowBuilder.Application.Queries.ExportWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Commands.BulkExportWorkflows;

public sealed class BulkExportWorkflowsHandler(IWorkflowRepository workflowRepo)
    : IQueryHandler<BulkExportWorkflowsCommand, IReadOnlyList<WorkflowExportDto>>
{
    public async Task<IReadOnlyList<WorkflowExportDto>> Handle(
        BulkExportWorkflowsCommand command, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkflowDefinition> workflows =
            await workflowRepo.GetAllAsync(command.workspaceId, cancellationToken);

        return workflows.Select(ExportWorkflowHandler.ToExportDto).ToList();
    }
}
