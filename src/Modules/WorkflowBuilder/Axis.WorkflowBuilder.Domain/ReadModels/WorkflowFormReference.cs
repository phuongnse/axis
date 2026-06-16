namespace Axis.WorkflowBuilder.Domain.ReadModels;

/// <summary>
/// Tracks Form steps that reference a form definition. <see cref="IsBroken"/> is set when the form is deleted (Kafka).
/// </summary>
public sealed class WorkflowFormReference
{
    public Guid WorkflowId { get; private set; }
    public Guid StepId { get; private set; }
    public Guid FormId { get; private set; }
    public Guid workspaceId { get; private set; }
    public bool IsBroken { get; private set; }

    private WorkflowFormReference() { } // EF Core materialisation

    public static WorkflowFormReference Create(
        Guid workflowId,
        Guid stepId,
        Guid formId,
        Guid workspaceId,
        bool isBroken = false)
        => new()
        {
            WorkflowId = workflowId,
            StepId = stepId,
            FormId = formId,
            workspaceId = workspaceId,
            IsBroken = isBroken,
        };

    public void MarkBroken() => IsBroken = true;

    public void MarkHealthy() => IsBroken = false;

    public void Retarget(Guid formId)
    {
        FormId = formId;
        IsBroken = false;
    }
}
