using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Queries.GetWorkflows;

/// <summary>US-048: Returns all workflow definitions for an organization.</summary>
public sealed record GetWorkflowsQuery(Guid OrganizationId) : IQuery<IReadOnlyList<WorkflowSummaryDto>>;
