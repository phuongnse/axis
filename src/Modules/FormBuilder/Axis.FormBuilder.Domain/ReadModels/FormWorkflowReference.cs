namespace Axis.FormBuilder.Domain.ReadModels;

/// <summary>
/// Local read model tracking which workflows reference each form.
/// Maintained via WorkflowPublished / WorkflowArchived / WorkflowUnarchived events
/// from WorkflowBuilder — never queried from WorkflowBuilder's database directly.
/// </summary>
public sealed class FormWorkflowReference
{
    public Guid WorkflowId { get; private set; }
    public Guid FormId { get; private set; }
    public Guid TeamAccountId { get; private set; }
    public bool IsActive { get; private set; }

    private FormWorkflowReference() { } // EF Core materialisation

    public static FormWorkflowReference Create(Guid workflowId, Guid formId, Guid teamAccountId)
        => new() { WorkflowId = workflowId, FormId = formId, TeamAccountId = teamAccountId, IsActive = true };

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
