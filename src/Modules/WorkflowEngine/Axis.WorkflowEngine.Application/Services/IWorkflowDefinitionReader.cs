namespace Axis.WorkflowEngine.Application.Services;

/// <summary>
/// Read-only cross-module boundary: WorkflowEngine reads workflow definition state
/// from WorkflowBuilder without taking a direct module dependency.
/// </summary>
public interface IWorkflowDefinitionReader
{
    Task<bool> IsActiveAsync(Guid workflowDefinitionId, Guid organizationId, CancellationToken ct = default);
}
