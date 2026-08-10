using System.Security.Cryptography;
using System.Text;
using Axis.Identity.Application;
using Axis.Identity.Application.Services;
using Axis.Shared.Domain.Primitives;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class RedisWorkspaceInvitationRateLimiter(
    IConnectionMultiplexer redis,
    IConfiguration configuration) : IWorkspaceInvitationRateLimiter
{
    private const string IncrementPartitions =
        """
        local exceeded = 0
        for index, key in ipairs(KEYS) do
          local count = redis.call('INCR', key)
          if count == 1 then
            redis.call('EXPIRE', key, ARGV[1])
          end
          if count > tonumber(ARGV[index + 1]) then
            exceeded = 1
          end
        end
        return exceeded
        """;

    private readonly int createPerInviterLimit = Positive(
        configuration.GetValue("Identity:Invitations:CreatePerInviterPerHour", 20));
    private readonly int createPerRecipientLimit = Positive(
        configuration.GetValue("Identity:Invitations:CreatePerRecipientPerHour", 5));
    private readonly int resendLimit = Positive(
        configuration.GetValue("Identity:Invitations:ResendPerInvitationPerHour", 3));
    private readonly int exchangePerPartitionLimit = Positive(
        configuration.GetValue("Identity:Invitations:ExchangePerPartitionPerHour", 30));
    private readonly int exchangePerTokenLimit = Positive(
        configuration.GetValue("Identity:Invitations:ExchangePerTokenPerHour", 5));

    public Task<Result> AcquireCreateAsync(
        Guid inviterUserId,
        Guid workspaceId,
        string normalizedEmail,
        CancellationToken ct = default) =>
        AcquireAsync(
            [
                $"identity:workspace-invite:create:actor:{inviterUserId:N}:workspace:{workspaceId:N}",
                $"identity:workspace-invite:create:recipient:{Digest(normalizedEmail)}:workspace:{workspaceId:N}",
            ],
            [createPerInviterLimit, createPerRecipientLimit]);

    public Task<Result> AcquireResendAsync(
        Guid inviterUserId,
        Guid invitationId,
        CancellationToken ct = default) =>
        AcquireAsync(
            [$"identity:workspace-invite:resend:{invitationId:N}:actor:{inviterUserId:N}"],
            [resendLimit]);

    public Task<Result> AcquireExchangeAsync(
        string requestPartition,
        string tokenHash,
        CancellationToken ct = default) =>
        AcquireAsync(
            [
                $"identity:workspace-invite:exchange:partition:{Digest(requestPartition)}",
                $"identity:workspace-invite:exchange:access:{Digest(tokenHash)}",
            ],
            [exchangePerPartitionLimit, exchangePerTokenLimit]);

    private async Task<Result> AcquireAsync(string[] keys, int[] limits)
    {
        IDatabase database = redis.GetDatabase();
        RedisKey[] redisKeys = keys.Select(key => (RedisKey)key).ToArray();
        RedisValue[] values = [(int)TimeSpan.FromHours(1).TotalSeconds, .. limits.Select(limit => (RedisValue)limit)];
        RedisResult result = await database.ScriptEvaluateAsync(
            IncrementPartitions,
            redisKeys,
            values);

        return (long)result == 0
            ? Result.Success()
            : Result.Failure(
                ErrorCodes.RateLimited,
                "Please wait before changing this invitation again.",
                IdentityProblemCodes.InvitationRateLimited);
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant())));

    private static int Positive(int value) =>
        value > 0 ? value : throw new InvalidOperationException("Invitation rate limits must be positive.");
}
