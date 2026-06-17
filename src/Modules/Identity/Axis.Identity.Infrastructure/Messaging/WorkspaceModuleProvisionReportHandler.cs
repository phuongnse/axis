using axis.identity.events;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Wolverine;

namespace Axis.Identity.Infrastructure.Messaging;

internal sealed class WorkspaceModuleProvisionReportHandler(
    IWorkspaceRepository WorkspaceRepo,
    IWorkspaceModuleProvisioningRepository provisioningRepo,
    IUnitOfWork uow,
    IMessageBus messageBus,
    IPlatformProvisioningAlert alert)
{
    public Task Handle(WorkspaceModuleProvisionReportEvent report, CancellationToken cancellationToken)
        => WorkspaceProvisioningCoordinator.HandleReportAsync(
            report,
            WorkspaceRepo,
            provisioningRepo,
            uow,
            messageBus,
            alert,
            cancellationToken);
}
