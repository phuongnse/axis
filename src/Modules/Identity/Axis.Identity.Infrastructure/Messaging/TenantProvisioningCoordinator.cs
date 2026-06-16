using axis.identity.events;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Wolverine;

namespace Axis.Identity.Infrastructure.Messaging;

internal static class TenantProvisioningCoordinator
{
    public const int MaxAttempts = 3;

    public static TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromSeconds(5 * Math.Pow(5, attempt - 1));

    public static async Task HandleReportAsync(
        TenantModuleProvisionReportEvent report,
        ITeamAccountRepository teamAccountRepo,
        ITenantModuleProvisioningRepository provisioningRepo,
        IUnitOfWork uow,
        IMessageBus messageBus,
        IPlatformProvisioningAlert alert,
        CancellationToken cancellationToken)
    {
        Guid teamAccountId = report.TeamAccountId();
        string module = report.module;

        TenantModuleProvisioning? row = await provisioningRepo.GetAsync(teamAccountId, module, cancellationToken);
        if (row is null)
            return;

        TeamAccount? teamAccount = await teamAccountRepo.GetByIdAsync(teamAccountId, cancellationToken);
        if (teamAccount is null)
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
                    new RetryTenantModuleProvisionMessage(teamAccountId, module, report.attempt + 1),
                    runAt);
                return;
            }

            teamAccount.MarkProvisioningFailed();
            await alert.AlertProvisioningFailedAsync(
                teamAccountId, module, report.attempt, error, cancellationToken);
        }

        await uow.SaveChangesAsync(cancellationToken);

        IReadOnlyList<TenantModuleProvisioning> all =
            await provisioningRepo.GetAllForTeamAccountAsync(teamAccountId, cancellationToken);

        if (TenantModuleNames.All.All(moduleName =>
                all.Any(p =>
                    p.Module == moduleName
                    && p.Status == TenantModuleProvisioningStatus.Succeeded))
            && teamAccount.Status == TeamAccountStatus.Provisioning)
        {
            teamAccount.CompleteProvisioning();
            await uow.SaveChangesAsync(cancellationToken);
        }
    }
}
