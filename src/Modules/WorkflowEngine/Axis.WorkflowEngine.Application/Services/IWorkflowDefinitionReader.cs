using Axis.WorkflowEngine.Domain.ReadModels;

namespace Axis.WorkflowEngine.Application.Services;

/// <summary>
/// Read-only cross-module boundary: WorkflowEngine reads workflow definition state
/// from WorkflowBuilder without taking a direct module dependency.
/// Backed by local read models maintained via Wolverine domain events.
/// </summary>
public interface IWorkflowDefinitionReader
{
    Task<bool> IsActiveAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken ct = default);

    /// <summary>
    /// Returns the workflow step/transition snapshot captured at last publish time,
    /// or null if the workflow has never been published.
    /// </summary>
    Task<WorkflowSnapshot?> GetSnapshotAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken ct = default);
}
