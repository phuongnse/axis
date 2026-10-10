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
}
