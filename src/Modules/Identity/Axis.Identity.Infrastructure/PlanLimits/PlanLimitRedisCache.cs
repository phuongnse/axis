using Axis.Shared.Application.PlanLimits;
using StackExchange.Redis;

namespace Axis.Identity.Infrastructure.PlanLimits;

public sealed class PlanLimitRedisCache(IConnectionMultiplexer redis)
{
    /// <summary>usage stats on settings page must be at most this stale.</summary>
    public static readonly TimeSpan UsageStatsMaxStaleness = TimeSpan.FromMinutes(5);

    private const string DecrementFloorZeroScript =
        """
        local v = redis.call('GET', KEYS[1])
        if not v then return 0 end
        local n = tonumber(v) + tonumber(ARGV[1])
        if n < 0 then n = 0 end
        redis.call('SET', KEYS[1], n, 'KEEPTTL')
        return n
        """;

    public async Task<long?> TryGetCachedUsageAsync(
        Guid teamAccountId,
        PlanLimitResourceType resourceType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            IDatabase db = redis.GetDatabase();
            RedisValue value = await db.StringGetAsync(BuildKey(teamAccountId, resourceType));
            if (!value.HasValue)
                return null;

            return (long)value;
        }
        catch (RedisException)
        {
            return null;
        }
    }

    public async Task<bool> TrySetCachedUsageAsync(
        Guid teamAccountId,
        PlanLimitResourceType resourceType,
        long usage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            IDatabase db = redis.GetDatabase();
            RedisKey key = BuildKey(teamAccountId, resourceType);
            return await db.StringSetAsync(key, usage, GetExpiry(resourceType));
        }
        catch (RedisException)
        {
            return false;
        }
    }

    public async Task<bool> TryAdjustUsageAsync(
        Guid teamAccountId,
        PlanLimitResourceType resourceType,
        int delta,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            IDatabase db = redis.GetDatabase();
            string key = BuildKey(teamAccountId, resourceType);
            if (delta >= 0)
            {
                long count = await db.StringIncrementAsync(key, delta);
                await db.KeyExpireAsync(key, GetExpiry(resourceType));
                return true;
            }

            await db.ScriptEvaluateAsync(
                DecrementFloorZeroScript,
                [(RedisKey)key],
                [(RedisValue)delta]);
            return true;
        }
        catch (RedisException)
        {
            return false;
        }
    }

    public async Task TryInvalidateTeamAccountAsync(Guid teamAccountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            IDatabase db = redis.GetDatabase();
            await db.KeyDeleteAsync(
            [
                BuildKey(teamAccountId, PlanLimitResourceType.Workflows),
                BuildKey(teamAccountId, PlanLimitResourceType.Users),
                BuildKey(teamAccountId, PlanLimitResourceType.ExecutionsPerMonth),
            ]);
        }
        catch (RedisException)
        {
            // Best-effort cache bust after plan change.
        }
    }

    private static string BuildKey(Guid teamAccountId, PlanLimitResourceType resourceType) =>
        resourceType switch
        {
            PlanLimitResourceType.Workflows => $"plan:{teamAccountId:N}:workflows",
            PlanLimitResourceType.Users => $"plan:{teamAccountId:N}:users",
            PlanLimitResourceType.ExecutionsPerMonth =>
                $"plan:{teamAccountId:N}:executions:{DateTime.UtcNow:yyyyMM}",
            _ => $"plan:{teamAccountId:N}:{resourceType}",
        };

    private static TimeSpan GetExpiry(PlanLimitResourceType resourceType)
    {
        if (resourceType == PlanLimitResourceType.ExecutionsPerMonth)
        {
            DateTime now = DateTime.UtcNow;
            DateTime nextMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
            TimeSpan untilMonthEnd = nextMonth - now;
            return untilMonthEnd < UsageStatsMaxStaleness ? untilMonthEnd : UsageStatsMaxStaleness;
        }

        return UsageStatsMaxStaleness;
    }
}
