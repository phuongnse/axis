namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.process_work_items</c>: work that is ready to run, or claimed by a worker
/// until its lease expires. Completed and failed items are deleted.
/// </summary>
public sealed class WorkItemRow
{
    public required Guid Id { get; init; }

    /// <summary>The tenant the work runs for. A worker claims only items of the tenant it polls.</summary>
    public required string TenantId { get; init; }

    /// <summary>Selects the handler that runs the item.</summary>
    public required string Kind { get; init; }

    /// <summary>The item is not claimed before this time.</summary>
    public required DateTimeOffset DueAt { get; init; }

    /// <summary>When the current claim ends, or <see langword="null"/> when the item was never claimed.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; init; }

    /// <summary>The token of the current claim, or <see langword="null"/> when the item was never claimed.</summary>
    public Guid? ClaimToken { get; init; }

    /// <summary>The process instance the item runs a step of, or <see langword="null"/> for other work.</summary>
    public Guid? ProcessInstanceId { get; init; }
}
