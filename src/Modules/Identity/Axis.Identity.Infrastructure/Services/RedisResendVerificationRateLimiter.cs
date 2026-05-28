using System.Security.Cryptography;
using System.Text;
using Axis.Identity.Application.Services;
using Axis.Shared.Domain.Primitives;
using StackExchange.Redis;

namespace Axis.Identity.Infrastructure.Services;

/// <summary>per-email resend cap with atomic INCR+EXPIRE (Lua; no PII in key).</summary>
internal sealed class RedisResendVerificationRateLimiter(IConnectionMultiplexer redis) : IResendVerificationRateLimiter
{
    private const int MaxResendsPerHour = 3;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private const string IncrementWithExpiryScript =
        """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then
          redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        return count
        """;

    public async Task<Result> TryRecordResendAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            return Result.Success();

        string key = BuildKey(normalizedEmail);
        IDatabase db = redis.GetDatabase();

        RedisResult scriptResult = await db.ScriptEvaluateAsync(
            IncrementWithExpiryScript,
            [(RedisKey)key],
            [(RedisValue)(int)Window.TotalSeconds]);

        long count = (long)scriptResult;
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
