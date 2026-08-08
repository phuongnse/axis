namespace Axis.Authorization.Application;

public interface IAuthorizationAuditHealthReader
{
    Task<AuthorizationAuditHealthSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed record AuthorizationAuditHealthSnapshot(
    int PoisonedCount,
    DateTimeOffset? OldestPendingAt);
