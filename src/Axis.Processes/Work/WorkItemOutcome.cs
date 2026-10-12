namespace Axis.Processes.Work;

/// <summary>How running a claimed work item ended.</summary>
public enum WorkItemOutcome
{
    /// <summary>The handler succeeded. Its writes and the item's deletion committed.</summary>
    Completed,

    /// <summary>The handler threw. Its writes rolled back, then its failure callback and the item's deletion committed.</summary>
    Failed,

    /// <summary>Another worker holds the item now. Nothing this worker wrote for it committed.</summary>
    LostClaim,

    /// <summary>The handler kept the item and made it due again later. Its writes and the new due time committed.</summary>
    Rescheduled,
}
