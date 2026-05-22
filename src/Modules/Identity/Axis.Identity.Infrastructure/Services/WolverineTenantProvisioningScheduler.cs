using Axis.Identity.Application.Services;
using Axis.Shared.Application.Tenancy;
using Wolverine;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class WolverineTenantProvisioningScheduler(IMessageBus bus) : ITenantProvisioningScheduler
{
    public Task EnqueueAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return bus.PublishAsync(new ProvisionTenantMessage(organizationId)).AsTask();
    }
}
