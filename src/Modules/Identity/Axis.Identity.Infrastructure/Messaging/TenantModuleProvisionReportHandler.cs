using axis.identity.events;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Wolverine;

namespace Axis.Identity.Infrastructure.Messaging;

internal sealed class TenantModuleProvisionReportHandler(
    ITeamAccountRepository teamAccountRepo,
    ITenantModuleProvisioningRepository provisioningRepo,
    IUnitOfWork uow,
    IMessageBus messageBus,
    IPlatformProvisioningAlert alert)
{
    public Task Handle(TenantModuleProvisionReportEvent report, CancellationToken cancellationToken)
        => TenantProvisioningCoordinator.HandleReportAsync(
            report,
            teamAccountRepo,
            provisioningRepo,
            uow,
            messageBus,
            alert,
            cancellationToken);
}
