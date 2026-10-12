using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Writes the connector calls that <c>callConnector</c> steps stage over a connection to a tenant
/// database, and names their states. A call's id is its operation identity.
/// </summary>
public static class ConnectorCalls
{
    /// <summary>The state of a call that waits for its next attempt.</summary>
    public const string Pending = "pending";

    /// <summary>
    /// Inserts <paramref name="call"/> as <c>pending</c> with no attempts. It is created at the
    /// transaction's time. A second call for the same instance and step violates the unique index.
    /// </summary>
    internal static async Task InsertAsync(NpgsqlTransaction transaction, NewConnectorCall call, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.connector_calls
                (id, process_instance_id, step, release_id, connector, operation, state, attempts, input, created_at)
            VALUES (@id, @instance, @step, @release, @connector, @operation, @state, 0, @input, now())
            """,
            transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction)),
            transaction);
        command.Parameters.AddWithValue("id", call.Id);
        command.Parameters.AddWithValue("instance", call.ProcessInstanceId);
        command.Parameters.AddWithValue("step", call.Step);
        command.Parameters.AddWithValue("release", call.ReleaseId);
        command.Parameters.AddWithValue("connector", call.Connector);
        command.Parameters.AddWithValue("operation", call.Operation);
        command.Parameters.AddWithValue("state", Pending);
        command.Parameters.Add(new NpgsqlParameter("input", NpgsqlDbType.Jsonb) { Value = call.Input.ToJsonString() });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>A connector call that a <c>callConnector</c> step stages for its instance.</summary>
/// <param name="Id">The call's operation identity, a version 7 UUID.</param>
/// <param name="Connector">The connector's declared name.</param>
/// <param name="Operation">The operation's declared name.</param>
/// <param name="Input">The evaluated input, keyed by the inputs' declared names.</param>
internal sealed record NewConnectorCall(
    Guid Id,
    Guid ProcessInstanceId,
    string Step,
    Guid ReleaseId,
    string Connector,
    string Operation,
    JsonObject Input);
