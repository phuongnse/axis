using System.Collections.Concurrent;
using Axis.Data.Storage;
using Axis.Tenancy;
using Npgsql;

namespace Axis.Agent;

/// <summary>A request whose state needs work, as the agent host lists it.</summary>
public sealed record AuthoringRequestToWork(Guid Id, string State, DateTimeOffset SentAt);

/// <summary>
/// The agent host's reads of <c>axis.authoring_requests</c> in a tenant database, see
/// <c>docs/reference/authoring-requests.md#durable-requests</c> and <c>docs/reference/authoring-requests.md#states</c>. It opens a tenant database with the tenant's runtime role only:
/// the first read of a tenant runs the <see cref="RuntimeRoleCheck"/> over the connection, and a role that fails it reads nothing.
/// </summary>
public sealed class AuthoringRequestStore(TenantConnectionFactory connections)
{
    private readonly ConcurrentDictionary<string, bool> _checked = new(StringComparer.Ordinal);

    /// <summary>Lists the requests of the tenant in a state that needs work, oldest first.</summary>
    /// <exception cref="InvalidOperationException">The tenant is not configured, or its role fails the runtime role check. The message names the tenant and the role.</exception>
    public async Task<IReadOnlyList<AuthoringRequestToWork>> ListNeedingWorkAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        await using var connection = await connections.OpenConnectionAsync(tenantId, cancellationToken);
        if (!_checked.ContainsKey(tenantId))
        {
            await RuntimeRoleCheck.CheckAsync(connection, tenantId, cancellationToken);
            _checked[tenantId] = true;
        }

        await using var command = new NpgsqlCommand(
            """
            SELECT id, state, sent_at
            FROM axis.authoring_requests
            WHERE state IN ('planning', 'working', 'checking', 'reviewing', 'fixing')
            ORDER BY sent_at, id
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var requests = new List<AuthoringRequestToWork>();
        while (await reader.ReadAsync(cancellationToken))
        {
            requests.Add(new AuthoringRequestToWork(reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2)));
        }

        return requests;
    }

    /// <summary>Drops the passed role check of the tenant, so the next read checks the role again over its new settings.</summary>
    public void Forget(string tenantId) => _checked.TryRemove(tenantId, out _);
}
