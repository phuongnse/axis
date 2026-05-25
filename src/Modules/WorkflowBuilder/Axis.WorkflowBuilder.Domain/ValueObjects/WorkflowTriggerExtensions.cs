using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.WorkflowBuilder.Domain.ValueObjects;

public static class WorkflowTriggerExtensions
{
    /// <summary>
    /// Extracts the modelId from an Event trigger config (record.* triggers).
    /// </summary>
    public static Guid? TryGetEventModelId(this WorkflowTrigger trigger)
    {
        if (trigger.Type != TriggerType.Event || trigger.Config is null)
            return null;

        if (!trigger.Config.TryGetValue("modelId", out object? raw) || raw is null)
            return null;

        return Guid.TryParse(raw.ToString(), out Guid id) ? id : null;
    }
}
