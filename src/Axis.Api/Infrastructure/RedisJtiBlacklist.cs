using StackExchange.Redis;

namespace Axis.Api.Infrastructure;

internal sealed class RedisJtiBlacklist(IConnectionMultiplexer redis) : IJtiBlacklist
{
    private static string Key(string jti) => $"jti:blacklist:{jti}";

    public async Task BlacklistAsync(string jti, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        await db.StringSetAsync(Key(jti), "1", ttl);
    }

    public async Task<bool> IsBlacklistedAsync(string jti, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        return await db.KeyExistsAsync(Key(jti));
    }
}
