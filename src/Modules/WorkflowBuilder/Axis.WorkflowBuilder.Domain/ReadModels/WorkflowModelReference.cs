namespace Axis.WorkflowBuilder.Domain.ReadModels;

/// <summary>
/// Tracks Event triggers scoped to a DataModeling model. <see cref="IsBroken"/> is set when the model is deleted (Kafka).
/// </summary>
public sealed class WorkflowModelReference
{
    public Guid WorkflowId { get; private set; }
    public Guid ModelId { get; private set; }
    public Guid TeamAccountId { get; private set; }
    public bool IsBroken { get; private set; }

    private WorkflowModelReference() { } // EF Core materialisation

    public static WorkflowModelReference Create(
        Guid workflowId,
        Guid modelId,
        Guid teamAccountId,
        bool isBroken = false)
        => new()
        {
            WorkflowId = workflowId,
            ModelId = modelId,
            TeamAccountId = teamAccountId,
            IsBroken = isBroken,
        };

    public void MarkBroken() => IsBroken = true;

    public void MarkHealthy() => IsBroken = false;

    public void Retarget(Guid modelId)
    {
        ModelId = modelId;
        IsBroken = false;
    }
}
