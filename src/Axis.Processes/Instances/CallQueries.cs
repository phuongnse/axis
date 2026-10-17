using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Reads the connector calls and package calls of a process instance, with their attempts and
/// lookups, over a connection to a tenant database. The trace id of an attempt is never read.
/// </summary>
public static class CallQueries
{
    /// <summary>
    /// Reads the connector calls and package calls of the instance <paramref name="instanceId"/>,
    /// oldest first, then by id, each with its attempts and lookups ordered by start time and then by id.
    /// </summary>
    public static async Task<IReadOnlyList<ProcessCallDetail>> ListByInstanceAsync(
        NpgsqlConnection connection,
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var calls = new List<ProcessCallDetail>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT id, 'connector' AS kind, step, connector AS name, NULL::text AS version, operation, state, input::text, created_at
            FROM axis.connector_calls WHERE process_instance_id = @id
            UNION ALL
            SELECT id, 'package' AS kind, step, package AS name, version, operation, state, input::text, created_at
            FROM axis.package_calls WHERE process_instance_id = @id
            ORDER BY created_at, id
            """,
            connection))
        {
            command.Parameters.AddWithValue("id", instanceId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                calls.Add(new ProcessCallDetail
                {
                    Id = reader.GetGuid(0),
                    Kind = reader.GetString(1),
                    Step = reader.GetString(2),
                    Name = reader.GetString(3),
                    Version = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Operation = reader.GetString(5),
                    State = reader.GetString(6),
                    Input = reader.IsDBNull(7) ? null : reader.GetString(7),
                    CreatedAt = reader.GetFieldValue<DateTimeOffset>(8),
                });
            }
        }

        if (calls.Count == 0)
        {
            return calls;
        }

        var attempts = new Dictionary<Guid, List<ProcessCallAttemptDetail>>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT call_id, kind, number, started_at, finished_at, outcome, http_status, error
            FROM axis.process_call_attempts WHERE call_id = ANY(@ids)
            ORDER BY started_at, id
            """,
            connection))
        {
            command.Parameters.AddWithValue("ids", calls.Select(call => call.Id).ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var callId = reader.GetGuid(0);
                if (!attempts.TryGetValue(callId, out var list))
                {
                    attempts[callId] = list = [];
                }

                list.Add(new ProcessCallAttemptDetail
                {
                    Kind = reader.GetString(1),
                    Number = reader.GetInt32(2),
                    StartedAt = reader.GetFieldValue<DateTimeOffset>(3),
                    FinishedAt = reader.GetFieldValue<DateTimeOffset>(4),
                    Outcome = reader.GetString(5),
                    HttpStatus = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    Error = reader.IsDBNull(7) ? null : reader.GetString(7),
                });
            }
        }

        foreach (var call in calls)
        {
            call.Attempts = attempts.GetValueOrDefault(call.Id) ?? [];
        }

        return calls;
    }
}

/// <summary>
/// A connector call or package call of an instance. <see cref="Name"/> is the connector of a
/// connector call and the package of a package call, which also has a <see cref="Version"/>.
/// <see cref="Input"/> is the stored evaluated input as JSON text, or null when none is stored.
/// </summary>
public sealed class ProcessCallDetail
{
    public required Guid Id { get; init; }

    /// <summary><c>connector</c> or <c>package</c>.</summary>
    public required string Kind { get; init; }

    public required string Step { get; init; }

    public required string Name { get; init; }

    /// <summary>The connector's declared name for a connector call, or null.</summary>
    public string? Connector => Kind == "connector" ? Name : null;

    /// <summary>The package id for a package call, or null.</summary>
    public string? Package => Kind == "package" ? Name : null;

    public string? Version { get; init; }

    public required string Operation { get; init; }

    public required string State { get; init; }

    public string? Input { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The attempts and lookups, ordered by start time and then by id.</summary>
    public IReadOnlyList<ProcessCallAttemptDetail> Attempts { get; set; } = [];
}

/// <summary>An attempt or a reconciliation lookup of a call.</summary>
public sealed class ProcessCallAttemptDetail
{
    /// <summary><c>attempt</c> or <c>lookup</c>.</summary>
    public required string Kind { get; init; }

    public required int Number { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset FinishedAt { get; init; }

    public required string Outcome { get; init; }

    public int? HttpStatus { get; init; }

    public string? Error { get; init; }
}
