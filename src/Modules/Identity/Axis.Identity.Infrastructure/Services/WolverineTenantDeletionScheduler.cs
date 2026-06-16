using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Wolverine;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class WolverineTenantDeletionScheduler(IMessageBus messageBus) : ITenantDeletionScheduler
{
    public async Task ScheduleHardDeleteAsync(Guid tenantId, DateTime hardDeleteAtUtc, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await messageBus.ScheduleAsync(
            new TenantHardDeleteJob(tenantId),
            new DateTimeOffset(hardDeleteAtUtc, TimeSpan.Zero));
    }
}
