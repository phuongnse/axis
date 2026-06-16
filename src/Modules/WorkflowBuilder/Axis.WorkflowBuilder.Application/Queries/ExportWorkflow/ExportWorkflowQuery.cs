using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Queries.ExportWorkflow;

public sealed record ExportWorkflowQuery(Guid WorkflowId, Guid tenantId) : IQuery<WorkflowExportDto?>;
