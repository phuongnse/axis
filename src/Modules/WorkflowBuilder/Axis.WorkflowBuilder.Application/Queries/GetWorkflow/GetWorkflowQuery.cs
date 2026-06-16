using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Queries.GetWorkflow;

public sealed record GetWorkflowQuery(Guid WorkflowId, Guid tenantId) : IQuery<WorkflowDetailDto?>;
