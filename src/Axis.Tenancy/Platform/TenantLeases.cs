using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Tenancy.Platform;

/// <summary>
/// Takes leases of a tenant's lease quotas, such as <c>concurrentQueries</c>, from <c>platform.tenant_leases</c>, which
/// every host of a cell shares, see <c>docs/reference/tenant-quotas.md#work-at-once</c>. A lease lasts
/// <see cref="Duration"/>, is renewed every <see cref="RenewInterval"/> while the work runs and is deleted when the work
/// ends, so a lease of a host that died expires within <see cref="Duration"/>. All times are the database clock's, so
/// every host sees the same expiry.
/// </summary>
public sealed partial class TenantLeases(NpgsqlDataSource platform, ILogger<TenantLeases> logger, TimeSpan? renewInterval = null)
{
    /// <summary>How long a lease lasts after it is taken or renewed.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(60);

    /// <summary>How often a lease is renewed while its work runs.</summary>
    public static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(20);

    private static readonly string _holder = $"{Environment.MachineName}:{Environment.ProcessId}";

    /// <summary>
    /// The <c>pg_advisory_xact_lock</c> key of a tenant's quota: the first eight bytes of a SHA-256 hash of both. A collision
    /// with another advisory lock only makes a take wait. It never lets one through.
    /// </summary>
    public static long Key(string tenantId, string quota) =>
        BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"axis.tenant-lease\0{tenantId}\0{quota}")));

    /// <summary>
    /// Takes one lease of <paramref name="quota"/> for the tenant, or returns <c>null</c> when <paramref name="limit"/>
    /// unexpired leases exist. Leases that have expired are deleted first. The take is one transaction under an advisory lock
    /// of the tenant and quota, so hosts that take at once never pass the limit. Disposing the lease releases it.
    /// </summary>
    /// <exception cref="NpgsqlException">The platform database cannot be reached.</exception>
    public async Task<TenantLease?> TryTakeAsync(string tenantId, string quota, long limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(quota);

        var id = Guid.NewGuid();
        await using var connection = await platform.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var serialize = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction))
        {
            serialize.Parameters.AddWithValue("key", Key(tenantId, quota));
            await serialize.ExecuteNonQueryAsync(cancellationToken);
        }

        int inserted;
        await using (var take = new NpgsqlCommand(
            """
            WITH expired AS (
                DELETE FROM platform.tenant_leases WHERE tenant_id = @tenant AND quota = @quota AND expires_at <= now())
            INSERT INTO platform.tenant_leases (lease_id, tenant_id, quota, holder, expires_at)
            SELECT @id, @tenant, @quota, @holder, now() + make_interval(secs => @seconds)
            WHERE (SELECT count(*) FROM platform.tenant_leases WHERE tenant_id = @tenant AND quota = @quota AND expires_at > now()) < @limit
            """,
            connection,
            transaction))
        {
            take.Parameters.AddWithValue("id", id);
            take.Parameters.AddWithValue("tenant", tenantId);
            take.Parameters.AddWithValue("quota", quota);
            take.Parameters.AddWithValue("holder", _holder);
            take.Parameters.AddWithValue("seconds", Duration.TotalSeconds);
            take.Parameters.AddWithValue("limit", limit);
            inserted = await take.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted == 0 ? null : new TenantLease(platform, logger, id, renewInterval ?? RenewInterval);
    }

    internal static void LogRenewFailed(ILogger logger, string cause) => RenewFailed(logger, cause);

    internal static void LogReleaseFailed(ILogger logger, string cause) => ReleaseFailed(logger, cause);

    [LoggerMessage(LogLevel.Warning, "A tenant lease could not be renewed over ConnectionStrings:Platform ({Cause}). It expires on its own.")]
    private static partial void RenewFailed(ILogger logger, string cause);

    [LoggerMessage(LogLevel.Warning, "A tenant lease could not be deleted over ConnectionStrings:Platform ({Cause}). It expires on its own.")]
    private static partial void ReleaseFailed(ILogger logger, string cause);
}

/// <summary>
/// One lease of a tenant's quota. It renews itself while it is held. Disposing it stops the renewal and deletes the row.
/// </summary>
public sealed class TenantLease : IAsyncDisposable
{
    private readonly NpgsqlDataSource _platform;
    private readonly ILogger _logger;
    private readonly Guid _id;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _renewal;

    internal TenantLease(NpgsqlDataSource platform, ILogger logger, Guid id, TimeSpan renewInterval)
    {
        _platform = platform;
        _logger = logger;
        _id = id;
        _renewal = RenewAsync(renewInterval, _stop.Token);
    }

    private async Task RenewAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await using var connection = await _platform.OpenConnectionAsync(cancellationToken);
                await using var command = new NpgsqlCommand(
                    "UPDATE platform.tenant_leases SET expires_at = now() + make_interval(secs => @seconds) WHERE lease_id = @id",
                    connection);
                command.Parameters.AddWithValue("seconds", TenantLeases.Duration.TotalSeconds);
                command.Parameters.AddWithValue("id", _id);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The lease was released.
        }
        catch (NpgsqlException exception)
        {
            // The lease expires on its own, and the work it guards goes on.
            TenantLeases.LogRenewFailed(_logger, exception.GetType().Name);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _renewal;
        _stop.Dispose();

        try
        {
            await using var connection = await _platform.OpenConnectionAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand("DELETE FROM platform.tenant_leases WHERE lease_id = @id", connection);
            command.Parameters.AddWithValue("id", _id);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (NpgsqlException exception)
        {
            TenantLeases.LogReleaseFailed(_logger, exception.GetType().Name);
        }
    }
}
