using System.Collections.Concurrent;
using Axis.Data.Storage;
using Axis.Tenancy;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Agent;

/// <summary>A request whose state needs work, as the agent host lists it.</summary>
public sealed record AuthoringRequestToWork(Guid Id, string State, DateTimeOffset SentAt);

/// <summary>
/// The agent host's reads and writes of <c>axis.authoring_requests</c> and <c>axis.authoring_request_steps</c> in a tenant database, see
/// <c>docs/reference/authoring-requests.md#durable-requests</c> and <c>docs/reference/authoring-requests.md#states</c>. It opens a tenant database with the tenant's runtime role only:
/// the first use of a tenant runs the <see cref="RuntimeRoleCheck"/> over the connection, and a role that fails it reads and writes nothing.
/// Every write after the claim is fenced by <c>claimed_by</c>, so a host that lost its claim, or whose request was dropped, changes nothing.
/// </summary>
public sealed class AuthoringRequestStore(TenantConnectionFactory connections)
{
    /// <summary>The states of a request that need work, see <c>docs/reference/authoring-requests.md#states</c>.</summary>
    public static readonly IReadOnlyList<string> StatesNeedingWork = ["planning", "working", "checking", "reviewing", "fixing"];

    /// <summary>Every state of a request, see <c>docs/reference/authoring-requests.md#states</c>.</summary>
    public static readonly IReadOnlyList<string> States =
        ["planning", "awaitingApproval", "working", "checking", "reviewing", "fixing", "ready", "accepted", "dropped", "failed"];

    private readonly ConcurrentDictionary<string, bool> _checked = new(StringComparer.Ordinal);

    /// <summary>Lists the requests of the tenant in a state that needs work, oldest first.</summary>
    /// <exception cref="InvalidOperationException">The tenant is not configured, or its role fails the runtime role check. The message names the tenant and the role.</exception>
    public async Task<IReadOnlyList<AuthoringRequestToWork>> ListNeedingWorkAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, state, sent_at
            FROM axis.authoring_requests
            WHERE state = ANY(@states)
            ORDER BY sent_at, id
            """,
            connection);
        command.Parameters.AddWithValue("states", StatesNeedingWork.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var requests = new List<AuthoringRequestToWork>();
        while (await reader.ReadAsync(cancellationToken))
        {
            requests.Add(ReadRequest(reader));
        }

        return requests;
    }

    /// <summary>
    /// Claims the oldest request of the tenant that is in one of the states and has no claim or a lapsed one. It sets <c>claimed_by</c> and
    /// <c>claimed_until</c>, and answers <c>null</c> when no request qualifies.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tenant is not configured, or its role fails the runtime role check.</exception>
    public async Task<AuthoringRequestToWork?> ClaimAsync(string tenantId, IReadOnlyCollection<string> states, string claimedBy, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimedBy);

        await using var connection = await OpenAsync(tenantId, cancellationToken);
        // clock_timestamp() and not now(): now() is fixed when the transaction starts, before the wait for the lock.
        await using var command = new NpgsqlCommand(
            """
            WITH next AS (
              SELECT r.id FROM axis.authoring_requests r
              WHERE r.state = ANY(@states) AND (r.claimed_until IS NULL OR r.claimed_until < clock_timestamp())
              ORDER BY r.sent_at, r.id LIMIT 1
              FOR UPDATE SKIP LOCKED)
            UPDATE axis.authoring_requests a
            SET claimed_by = @me, claimed_until = clock_timestamp() + @lease
            FROM next WHERE a.id = next.id
            RETURNING a.id, a.state, a.sent_at
            """,
            connection);
        command.Parameters.AddWithValue("states", states.ToArray());
        command.Parameters.AddWithValue("me", claimedBy);
        command.Parameters.AddWithValue("lease", lease);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRequest(reader) : null;
    }

    /// <summary>Renews the lease of the claim, and answers <c>false</c> when the host no longer holds it.</summary>
    public async Task<bool> RenewAsync(string tenantId, Guid requestId, string claimedBy, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE axis.authoring_requests SET claimed_until = clock_timestamp() + @lease WHERE id = @id AND claimed_by = @me",
            connection);
        command.Parameters.AddWithValue("lease", lease);
        command.Parameters.AddWithValue("id", requestId);
        command.Parameters.AddWithValue("me", claimedBy);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <summary>
    /// Closes the steps of the request that never finished, because an earlier host stopped, with the outcome <c>interrupted</c>. The tokens
    /// they already hold stay. It answers <c>false</c> when the host no longer holds the claim.
    /// </summary>
    public async Task<bool> CloseInterruptedAsync(string tenantId, Guid requestId, string claimedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(tenantId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await LockClaimAsync(connection, transaction, requestId, claimedBy, cancellationToken))
        {
            return false;
        }

        await using (var command = new NpgsqlCommand(
            "UPDATE axis.authoring_request_steps SET outcome = 'interrupted', finished_at = now() WHERE request_id = @id AND finished_at IS NULL",
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("id", requestId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>Adds a step row of the kind after the last one, and answers its id, or <c>null</c> when the host no longer holds the claim.</summary>
    public async Task<Guid?> StartStepAsync(string tenantId, Guid requestId, string claimedBy, string kind, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(tenantId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await LockClaimAsync(connection, transaction, requestId, claimedBy, cancellationToken))
        {
            return null;
        }

        var stepId = Guid.CreateVersion7();
        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO axis.authoring_request_steps (id, request_id, ordinal, kind, started_at)
            SELECT @step, @id, coalesce(max(ordinal), 0) + 1, @kind, now()
            FROM axis.authoring_request_steps WHERE request_id = @id
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("step", stepId);
            command.Parameters.AddWithValue("id", requestId);
            command.Parameters.AddWithValue("kind", kind);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return stepId;
    }

    /// <summary>Adds the tokens to the step and to the request in one transaction.</summary>
    /// <exception cref="AuthoringClaimLostException">The host no longer holds the claim. Nothing is written.</exception>
    public async Task AddTokensAsync(string tenantId, Guid requestId, Guid stepId, string claimedBy, long tokens, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tokens);

        await using var connection = await OpenAsync(tenantId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(
            "UPDATE axis.authoring_requests SET tokens = tokens + @tokens WHERE id = @id AND claimed_by = @me",
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("tokens", tokens);
            command.Parameters.AddWithValue("id", requestId);
            command.Parameters.AddWithValue("me", claimedBy);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new AuthoringClaimLostException();
            }
        }

        await using (var command = new NpgsqlCommand(
            "UPDATE axis.authoring_request_steps SET tokens = tokens + @tokens WHERE id = @step AND request_id = @id",
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("tokens", tokens);
            command.Parameters.AddWithValue("step", stepId);
            command.Parameters.AddWithValue("id", requestId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Commits a finished step with the request's next state in one transaction. The host keeps the claim when <paramref name="keepClaim"/> is
    /// <c>true</c>, extending the lease by <paramref name="lease"/>, and frees the request otherwise. It answers <c>false</c> and writes nothing
    /// when the host no longer holds the claim.
    /// </summary>
    public async Task<bool> FinishStepAsync(
        string tenantId,
        Guid requestId,
        Guid stepId,
        string claimedBy,
        string nextState,
        string? outcome,
        string? inputSummary,
        bool keepClaim,
        TimeSpan lease,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(tenantId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(
            """
            UPDATE axis.authoring_requests
            SET state = @next,
                finished_at = CASE WHEN @next = 'failed' THEN now() ELSE finished_at END,
                claimed_by = CASE WHEN @keep THEN claimed_by END,
                claimed_until = CASE WHEN @keep THEN clock_timestamp() + @lease END
            WHERE id = @id AND claimed_by = @me
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("next", nextState);
            command.Parameters.AddWithValue("keep", keepClaim);
            command.Parameters.AddWithValue("lease", lease);
            command.Parameters.AddWithValue("id", requestId);
            command.Parameters.AddWithValue("me", claimedBy);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await using (var command = new NpgsqlCommand(
            "UPDATE axis.authoring_request_steps SET outcome = @outcome, input_summary = @summary, finished_at = now() WHERE id = @step AND request_id = @id",
            connection,
            transaction))
        {
            command.Parameters.Add(new NpgsqlParameter("outcome", NpgsqlDbType.Text) { Value = (object?)outcome ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("summary", NpgsqlDbType.Text) { Value = (object?)inputSummary ?? DBNull.Value });
            command.Parameters.AddWithValue("step", stepId);
            command.Parameters.AddWithValue("id", requestId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>Drops the passed role check of the tenant, so the next use checks the role again over its new settings.</summary>
    public void Forget(string tenantId) => _checked.TryRemove(tenantId, out _);

    private static AuthoringRequestToWork ReadRequest(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2));

    // Locks the request's row when the host still holds the claim, so a drop or a claim by another host waits for the transaction.
    private static async Task<bool> LockClaimAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid requestId, string claimedBy, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM axis.authoring_requests WHERE id = @id AND claimed_by = @me FOR UPDATE",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", requestId);
        command.Parameters.AddWithValue("me", claimedBy);
        if (await command.ExecuteScalarAsync(cancellationToken) is not null)
        {
            return true;
        }

        await transaction.RollbackAsync(cancellationToken);
        return false;
    }

    private async Task<NpgsqlConnection> OpenAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var connection = await connections.OpenConnectionAsync(tenantId, cancellationToken);
        try
        {
            if (!_checked.ContainsKey(tenantId))
            {
                await RuntimeRoleCheck.CheckAsync(connection, tenantId, cancellationToken);
                _checked[tenantId] = true;
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
