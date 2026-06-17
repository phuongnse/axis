using Axis.Shared.Application.CQRS;
using Axis.WorkflowEngine.Application.DTOs;

namespace Axis.WorkflowEngine.Application.Queries.GetExecution;

/// <summary>Returns execution detail with step timeline.</summary>
public sealed record GetExecutionQuery(
    Guid ExecutionId,
    Guid workspaceId) : IQuery<ExecutionResponse?>;
