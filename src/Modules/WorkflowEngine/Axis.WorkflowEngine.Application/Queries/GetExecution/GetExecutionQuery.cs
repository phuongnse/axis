using Axis.Shared.Application.CQRS;
using Axis.WorkflowEngine.Application.DTOs;

namespace Axis.WorkflowEngine.Application.Queries.GetExecution;

/// <summary>US-091/US-098: Returns execution detail with step timeline.</summary>
public sealed record GetExecutionQuery(
    Guid ExecutionId,
    Guid OrganizationId) : IQuery<ExecutionResponse?>;
