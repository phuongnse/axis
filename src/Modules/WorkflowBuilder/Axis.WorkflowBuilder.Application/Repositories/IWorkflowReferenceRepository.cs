namespace Axis.WorkflowBuilder.Application.Repositories;

public interface IWorkflowReferenceRepository
{
    /// <summary>Persisted read-model check (SQL). After <see cref="IWorkflowReferenceSync.SyncAsync"/>, use <see cref="WorkflowReferenceSyncResult.HasBrokenReferences"/> instead.</summary>
    Task<bool> HasBrokenReferencesAsync(Guid workflowId, CancellationToken cancellationToken = default);

    Task<int> CountBlockingFormReferencesAsync(
        Guid formId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetBrokenStepIdsAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetBrokenModelIdsAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default);
}
