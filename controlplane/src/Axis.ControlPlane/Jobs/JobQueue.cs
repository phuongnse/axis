using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace Axis.ControlPlane.Jobs;

/// <summary>A job that a runner claimed. <see cref="Attempt"/> is the claim count, which fences every update of the claim.</summary>
internal sealed record ClaimedJob(Guid Id, string Kind, JsonObject Payload, int Attempt);

/// <summary>
/// Reads and writes <c>control_plane.jobs</c> over a connection. Times use <c>clock_timestamp()</c> of the database, so runners
/// on different machines agree when a lease ends. <c>now()</c> would be fixed when the transaction starts, before the wait for a lock.
/// </summary>
internal static class JobQueue
{
    // The claim is a materialized CTE and not a subquery in FROM: the planner may run such a subquery again for each row it
    // joins, and then the update claims more than one job while the reader returns only the first.
    private const string ClaimSql = """
        WITH c AS MATERIALIZED (
            SELECT id FROM control_plane.jobs
            WHERE kind = ANY(@kinds)
              AND ((state = 'queued' AND next_attempt_at <= clock_timestamp())
                   OR (state = 'running' AND lease_until <= clock_timestamp()))
            ORDER BY next_attempt_at, id LIMIT 1 FOR UPDATE SKIP LOCKED)
        UPDATE control_plane.jobs j
        SET state = 'running', attempts = j.attempts + 1, lease_owner = @owner, lease_until = clock_timestamp() + @lease
        FROM c
        WHERE j.id = c.id
        RETURNING j.id, j.kind, j.payload::text, j.attempts
        """;

    // Every update after the claim names the attempt count, which goes up on every claim: a runner that lost its claim changes no row.
    private const string RenewSql = """
        UPDATE control_plane.jobs
        SET lease_until = clock_timestamp() + @lease
        WHERE id = @id AND state = 'running' AND attempts = @attempt
        """;

    private const string SucceedSql = """
        UPDATE control_plane.jobs
        SET state = 'succeeded', finished_at = clock_timestamp(), lease_owner = NULL, lease_until = NULL
        WHERE id = @id AND state = 'running' AND attempts = @attempt
        """;

    private const string RetrySql = """
        UPDATE control_plane.jobs
        SET state = 'queued', next_attempt_at = clock_timestamp() + @delay, last_error = @error, lease_owner = NULL, lease_until = NULL
        WHERE id = @id AND state = 'running' AND attempts = @attempt
        """;

    private const string FailSql = """
        UPDATE control_plane.jobs
        SET state = 'failed', finished_at = clock_timestamp(), last_error = @error, lease_owner = NULL, lease_until = NULL
        WHERE id = @id AND state = 'running' AND attempts = @attempt
        """;

    /// <summary>
    /// Adds a queued job of <paramref name="kind"/> and returns its id. It is due at <paramref name="notBefore"/>, or now. Pass the
    /// <paramref name="transaction"/> of the change that needs the job, so both commit or neither does.
    /// </summary>
    public static async Task<Guid> EnqueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string kind,
        JsonObject payload,
        DateTimeOffset? notBefore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(payload);
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO control_plane.jobs (id, kind, payload, next_attempt_at)
            VALUES (@id, @kind, @payload, coalesce(@notBefore, clock_timestamp()))
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload.ToJsonString() });
        command.Parameters.Add(new NpgsqlParameter("notBefore", NpgsqlDbType.TimestampTz) { Value = notBefore is { } at ? at.ToUniversalTime() : DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    /// <summary>
    /// Claims the oldest due job of one of <paramref name="kinds"/>: a queued job whose time has come, or a running job whose lease
    /// ended. The claim counts one attempt. It returns <see langword="null"/> when no job is due.
    /// </summary>
    public static async Task<ClaimedJob?> ClaimAsync(
        NpgsqlConnection connection,
        string owner,
        IReadOnlyCollection<string> kinds,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(kinds);
        await using var command = new NpgsqlCommand(ClaimSql, connection);
        command.Parameters.AddWithValue("kinds", kinds.ToArray());
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("lease", lease);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var payload = JsonNode.Parse(reader.GetString(2)) as JsonObject ?? [];
        return new ClaimedJob(reader.GetGuid(0), reader.GetString(1), payload, reader.GetInt32(3));
    }

    /// <summary>Extends the lease. It returns <see langword="false"/> when the claim is gone, because the job ended or another runner claimed it.</summary>
    public static async Task<bool> RenewAsync(NpgsqlConnection connection, ClaimedJob job, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using var command = Fenced(RenewSql, connection, job);
        command.Parameters.AddWithValue("lease", lease);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Ends the job <c>succeeded</c>. It returns <see langword="false"/> when the claim is gone.</summary>
    public static async Task<bool> SucceedAsync(NpgsqlConnection connection, ClaimedJob job, CancellationToken cancellationToken)
    {
        await using var command = Fenced(SucceedSql, connection, job);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Queues the job again after <paramref name="delay"/>, with the error of the attempt. It returns <see langword="false"/> when the claim is gone.</summary>
    public static async Task<bool> RetryAsync(NpgsqlConnection connection, ClaimedJob job, TimeSpan delay, string error, CancellationToken cancellationToken)
    {
        await using var command = Fenced(RetrySql, connection, job);
        command.Parameters.AddWithValue("delay", delay);
        command.Parameters.AddWithValue("error", error);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Ends the job <c>failed</c> with the error of the attempt. It returns <see langword="false"/> when the claim is gone.</summary>
    public static async Task<bool> FailAsync(NpgsqlConnection connection, ClaimedJob job, string error, CancellationToken cancellationToken)
    {
        await using var command = Fenced(FailSql, connection, job);
        command.Parameters.AddWithValue("error", error);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static NpgsqlCommand Fenced(string sql, NpgsqlConnection connection, ClaimedJob job)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(job);
        var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", job.Id);
        command.Parameters.AddWithValue("attempt", job.Attempt);
        return command;
    }
}
