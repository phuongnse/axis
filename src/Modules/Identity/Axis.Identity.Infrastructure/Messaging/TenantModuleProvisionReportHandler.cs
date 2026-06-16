using axis.identity.events;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Wolverine;

namespace Axis.Identity.Infrastructure.Messaging;

internal sealed class TenantModuleProvisionReportHandler(
    ITenantRepository TenantRepo,
    ITenantModuleProvisioningRepository provisioningRepo,
    IUnitOfWork uow,
    IMessageBus messageBus,
    IPlatformProvisioningAlert alert)
{
    public Task Handle(TenantModuleProvisionReportEvent report, CancellationToken cancellationToken)
        => TenantProvisioningCoordinator.HandleReportAsync(
            report,
            TenantRepo,
            provisioningRepo,
            uow,
            messageBus,
            alert,
            cancellationToken);
}
