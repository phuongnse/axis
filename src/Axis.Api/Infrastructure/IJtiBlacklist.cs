namespace Axis.Api.Infrastructure;

public interface IJtiBlacklist
{
    Task BlacklistAsync(string jti, TimeSpan ttl, CancellationToken ct = default);
    Task<bool> IsBlacklistedAsync(string jti, CancellationToken ct = default);
}
