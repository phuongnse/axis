using Npgsql;

namespace Axis.Tenancy.Platform;

/// <summary>
/// Counts what a cell's worker does for a tenant per UTC day in <c>platform.tenant_daily_counters</c>, over the platform
/// database. A counter can have a limit, which one statement checks and counts in together, so workers that count at the
/// same time cannot pass it. The platform role that the data source connects as has the <c>INSERT</c> and <c>UPDATE</c>
/// grant of <see cref="PlatformRoleGrants"/> on the table, and no <c>DELETE</c>.
/// </summary>
public sealed class TenantDailyCounterStore(NpgsqlDataSource platform)
{
    /// <summary>The counter of the emails a worker sends.</summary>
    public const string Emails = "emails";

    /// <summary>The counter of the emails-held notice, at most one a day.</summary>
    public const string EmailsHeldNotice = "emailsHeldNotice";

    /// <summary>
    /// Adds one to the counter of the tenant on <paramref name="day"/> and returns <see langword="true"/>, unless the
    /// counter is at <paramref name="limit"/> already. With a <see langword="null"/> limit it always counts. With a limit
    /// of 0 or less it returns <see langword="false"/> and writes nothing. The day's row is created when it does not exist yet.
    /// </summary>
    public async Task<bool> TryIncrementAsync(string tenantId, string counter, DateOnly day, long? limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(counter);
        if (limit <= 0)
        {
            return false;
        }

        await using var command = platform.CreateCommand(
            """
            INSERT INTO platform.tenant_daily_counters AS c (tenant_id, day, counter, value)
            VALUES (@tenant, @day, @counter, 1)
            ON CONFLICT (tenant_id, day, counter) DO UPDATE SET value = c.value + 1
            WHERE @limit::bigint IS NULL OR c.value < @limit
            RETURNING value
            """);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("day", day);
        command.Parameters.AddWithValue("counter", counter);
        command.Parameters.Add(new NpgsqlParameter<long?>("limit", limit));
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    /// <summary>The counter of the tenant on <paramref name="day"/>, or 0 when it has no row.</summary>
    public async Task<long> ValueAsync(string tenantId, string counter, DateOnly day, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(counter);
        await using var command = platform.CreateCommand(
            "SELECT value FROM platform.tenant_daily_counters WHERE tenant_id = @tenant AND day = @day AND counter = @counter");
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("day", day);
        command.Parameters.AddWithValue("counter", counter);
        return await command.ExecuteScalarAsync(cancellationToken) as long? ?? 0;
    }

    /// <summary>
    /// Takes one back from the counter of the tenant on <paramref name="day"/>. The counter never goes below 0.
    /// </summary>
    public async Task DecrementAsync(string tenantId, string counter, DateOnly day, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(counter);
        await using var command = platform.CreateCommand(
            """
            UPDATE platform.tenant_daily_counters
            SET value = value - 1
            WHERE tenant_id = @tenant AND day = @day AND counter = @counter AND value > 0
            """);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("day", day);
        command.Parameters.AddWithValue("counter", counter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
