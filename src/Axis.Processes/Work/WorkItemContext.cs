using Npgsql;

namespace Axis.Processes.Work;

/// <summary>
/// What a handler runs with: the claimed item and an open transaction on the item's tenant database.
/// The handler's writes commit or roll back together with the item's completion.
/// </summary>
public sealed record WorkItemContext(ClaimedWorkItem Item, NpgsqlConnection Connection, NpgsqlTransaction Transaction);
