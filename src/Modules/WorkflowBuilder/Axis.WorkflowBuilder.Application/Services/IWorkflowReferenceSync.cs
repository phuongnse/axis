using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Services;

public interface IWorkflowReferenceSync
{
    /// <summary>
    /// Updates form/model reference rows for <paramref name="workflow"/> and reports whether any
    /// remaining reference is broken (evaluated on the in-memory read model, before save).
    /// </summary>
    Task<WorkflowReferenceSyncResult> SyncAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default);
}
