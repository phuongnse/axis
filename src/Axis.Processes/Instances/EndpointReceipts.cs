using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Writes and reads the receipt of an endpoint call that carried an <c>Idempotency-Key</c>, over a
/// connection to a tenant database. The receipt holds the record and the instance, if any, the call created,
/// and a hash of its input. The input is hashed in its <c>jsonb</c> text form, so two inputs with the
/// same JSON value have the same hash whatever their whitespace and property order.
/// </summary>
public static class EndpointReceipts
{
    /// <summary>
    /// Stores the receipt of <paramref name="key"/> unless one exists. Returns
    /// <see langword="false"/> when the key is taken. A concurrent claim of the same key waits for
    /// the first one's transaction, so only one of them inserts.
    /// </summary>
    public static async Task<bool> TryClaimAsync(
        NpgsqlTransaction transaction,
        Guid applicationId,
        Guid endpointId,
        string caller,
        string key,
        Guid recordId,
        Guid? instanceId,
        string inputJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.endpoint_receipts (application_id, endpoint_id, caller, key, record_id, instance_id, input_hash)
            VALUES (@application, @endpoint, @caller, @key, @record, @instance, sha256(convert_to(@input::text, 'UTF8')))
            ON CONFLICT (application_id, endpoint_id, caller, key) DO NOTHING
            """,
            transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction)),
            transaction);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("endpoint", endpointId);
        command.Parameters.AddWithValue("caller", caller);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("record", recordId);
        command.Parameters.AddWithValue("instance", (object?)instanceId ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("input", NpgsqlDbType.Jsonb) { Value = inputJson });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>The read of one receipt by its key, with the parameters <c>@application</c>, <c>@endpoint</c>, <c>@caller</c>, <c>@key</c> and <c>@input</c>.</summary>
    internal const string FindSql =
        """
        SELECT record_id, instance_id, input_hash = sha256(convert_to(@input::text, 'UTF8'))
        FROM axis.endpoint_receipts
        WHERE application_id = @application AND endpoint_id = @endpoint AND caller = @caller AND key = @key
        """;

    /// <summary>
    /// Returns the stored receipt of <paramref name="key"/>, and whether <paramref name="inputJson"/>
    /// is the input it was claimed with, or <see langword="null"/> when there is none.
    /// </summary>
    public static async Task<EndpointReceipt?> FindAsync(
        NpgsqlConnection connection,
        Guid applicationId,
        Guid endpointId,
        string caller,
        string key,
        string inputJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = new NpgsqlCommand(FindSql, connection);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("endpoint", endpointId);
        command.Parameters.AddWithValue("caller", caller);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.Add(new NpgsqlParameter("input", NpgsqlDbType.Jsonb) { Value = inputJson });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new EndpointReceipt(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetBoolean(2))
            : null;
    }
}

/// <summary>
/// A stored endpoint call: the record and the instance it created, which is null for an endpoint
/// without a process, and whether a repeat has the same input.
/// </summary>
public sealed record EndpointReceipt(Guid RecordId, Guid? InstanceId, bool SameInput);
