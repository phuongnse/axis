using Axis.Shared.Application.CQRS;
using Axis.WorkflowBuilder.Application.Queries.ExportWorkflow;

namespace Axis.WorkflowBuilder.Application.Commands.BulkExportWorkflows;

public sealed record BulkExportWorkflowsCommand(Guid tenantId) : IQuery<IReadOnlyList<WorkflowExportDto>>;
