namespace Axis.WorkflowBuilder.Application.Repositories;

public interface IWorkflowReferenceRepository
{
    Task<bool> HasBrokenReferencesAsync(Guid workflowId, CancellationToken cancellationToken = default);

    Task<int> CountBlockingFormReferencesAsync(
        Guid formId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetBrokenStepIdsAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetBrokenModelIdsAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default);
}
