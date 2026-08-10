using Axis.Authorization.Application;
using Axis.Authorization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Authorization.Infrastructure.Repositories;

internal sealed class AuthorizationAuditHealthReader(AuthorizationDbContext context)
    : IAuthorizationAuditHealthReader
{
    public async Task<AuthorizationAuditHealthSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        int poisoned = await context.AuditOutbox.CountAsync(
            row => row.DeliveryState == "Poisoned",
            cancellationToken);
        DateTimeOffset? oldestPending = await context.AuditOutbox
            .Where(row => row.DeliveryState == "Pending")
            .MinAsync(row => (DateTimeOffset?)row.CreatedAt, cancellationToken);
        return new AuthorizationAuditHealthSnapshot(poisoned, oldestPending);
    }
}
