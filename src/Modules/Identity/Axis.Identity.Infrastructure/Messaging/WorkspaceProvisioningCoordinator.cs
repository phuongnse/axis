using axis.identity.events;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Wolverine;

namespace Axis.Identity.Infrastructure.Messaging;

internal static class WorkspaceProvisioningCoordinator
{
    public const int MaxAttempts = 3;

    public static TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromSeconds(5 * Math.Pow(5, attempt - 1));

    public static async Task HandleReportAsync(
        WorkspaceModuleProvisionReportEvent report,
        IWorkspaceRepository WorkspaceRepo,
        IWorkspaceModuleProvisioningRepository provisioningRepo,
        IUnitOfWork uow,
        IMessageBus messageBus,
        IPlatformProvisioningAlert alert,
        CancellationToken cancellationToken)
    {
        Guid workspaceId = report.workspaceId();
        string module = report.module;

        WorkspaceModuleProvisioning? row = await provisioningRepo.GetAsync(workspaceId, module, cancellationToken);
        if (row is null)
            return;

        Workspace? Workspace = await WorkspaceRepo.GetByIdAsync(workspaceId, cancellationToken);
        if (Workspace is null)
            return;

        if (report.succeeded)
        {
            row.RecordSuccess();
        }
        else
        {
            string error = report.errorMessage ?? "Unknown provisioning error.";
            row.RecordFailure(error, report.attempt);

            if (report.attempt < MaxAttempts)
            {
                row.MarkRetryScheduled(report.attempt + 1);
                await uow.SaveChangesAsync(cancellationToken);

                DateTimeOffset runAt = DateTimeOffset.UtcNow.Add(RetryDelay(report.attempt + 1));
                await messageBus.ScheduleAsync(
                    new RetryWorkspaceModuleProvisionMessage(workspaceId, module, report.attempt + 1),
                    runAt);
                return;
            }

            Workspace.MarkProvisioningFailed();
            await alert.AlertProvisioningFailedAsync(
                workspaceId, module, report.attempt, error, cancellationToken);
        }

        await uow.SaveChangesAsync(cancellationToken);

        IReadOnlyList<WorkspaceModuleProvisioning> all =
            await provisioningRepo.GetAllForWorkspaceAsync(workspaceId, cancellationToken);

        if (WorkspaceModuleNames.All.All(moduleName =>
                all.Any(p =>
                    p.Module == moduleName
                    && p.Status == WorkspaceModuleProvisioningStatus.Succeeded))
            && Workspace.Status == WorkspaceStatus.Provisioning)
        {
            Workspace.CompleteProvisioning();
            await uow.SaveChangesAsync(cancellationToken);
        }
    }
}
