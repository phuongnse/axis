using Npgsql;

namespace Axis.Processes.Work;

/// <summary>
/// Runs the work items of one kind that call an external system, so no transaction may be open
/// while they run, or that commit in several transactions. The tenant context is the item's tenant
/// while it runs.
/// </summary>
public interface IExternalWorkItemHandler
{
    /// <summary>The work item kind this handler runs. Each kind has at most one handler of either sort.</summary>
    string Kind { get; }

    /// <summary>
    /// Runs the item with an open connection and no transaction. Each transaction the handler opens
    /// must check the claim token, and the handler deletes the item itself.
    /// </summary>
    Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, NpgsqlConnection connection, CancellationToken cancellationToken);
}
