using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Wolverine;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class WolverineOrganizationDeletionScheduler(IMessageBus messageBus) : IOrganizationDeletionScheduler
{
    public async Task ScheduleHardDeleteAsync(Guid organizationId, DateTime hardDeleteAtUtc, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await messageBus.ScheduleAsync(
            new OrganizationHardDeleteJob(organizationId),
            new DateTimeOffset(hardDeleteAtUtc, TimeSpan.Zero));
    }
}
