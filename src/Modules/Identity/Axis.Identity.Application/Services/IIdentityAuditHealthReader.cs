namespace Axis.Identity.Application.Services;

public interface IIdentityAuditHealthReader
{
    Task<IdentityAuditHealthSnapshot> ReadAsync(CancellationToken ct = default);
}

public sealed record IdentityAuditHealthSnapshot(
    int PoisonedCount,
    DateTimeOffset? OldestPendingAt);
