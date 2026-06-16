using axis.identity.events;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Wolverine;

namespace Axis.Identity.Infrastructure.Messaging;

internal sealed class TenantModuleProvisionReportHandler(
    IOrganizationRepository organizationRepo,
    ITenantModuleProvisioningRepository provisioningRepo,
    IUnitOfWork uow,
    IMessageBus messageBus,
    IPlatformProvisioningAlert alert)
{
    public Task Handle(TenantModuleProvisionReportEvent report, CancellationToken cancellationToken)
        => TenantProvisioningCoordinator.HandleReportAsync(
            report,
            organizationRepo,
            provisioningRepo,
            uow,
            messageBus,
            alert,
            cancellationToken);
}
