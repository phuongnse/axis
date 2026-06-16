namespace Axis.WorkflowEngine.Domain.Aggregates;

/// <summary>
/// Local read model tracking whether a workflow definition is currently active.
/// Maintained via WorkflowPublished / WorkflowArchived / WorkflowUnarchived events
/// from WorkflowBuilder — never queried from WorkflowBuilder's database directly.
/// </summary>
public sealed class WorkflowActiveStatus
{
    public Guid WorkflowId { get; private set; }
    public Guid tenantId { get; private set; }
    public bool IsActive { get; private set; }

    private WorkflowActiveStatus() { } // EF Core materialisation

    public static WorkflowActiveStatus Activated(Guid workflowId, Guid tenantId)
        => new() { WorkflowId = workflowId, tenantId = tenantId, IsActive = true };

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
