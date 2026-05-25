using System.Security.Cryptography;
using System.Text;
using Axis.Identity.Application.Services;
using Axis.Shared.Domain.Primitives;
using StackExchange.Redis;

namespace Axis.Identity.Infrastructure.Services;

/// <summary>US-002: sliding window via Redis INCR + 1h TTL (no email in key — SHA-256 hash only).</summary>
internal sealed class RedisResendVerificationRateLimiter(IConnectionMultiplexer redis) : IResendVerificationRateLimiter
{
    private const int MaxResendsPerHour = 3;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    public async Task<Result> TryRecordResendAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            return Result.Success();

        string key = BuildKey(normalizedEmail);
        IDatabase db = redis.GetDatabase();

        long count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, Window);

        if (count > MaxResendsPerHour)
        {
            return Result.Failure(
                ErrorCodes.RateLimited,
                "Please wait before requesting another email.");
        }

        return Result.Success();
    }

    private static string BuildKey(string normalizedEmail)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail.Trim().ToLowerInvariant()));
        return $"identity:verify-resend:{Convert.ToHexString(hash)}";
    }
}
