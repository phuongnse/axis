using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.TeamAccounts;
using Axis.Shared.Infrastructure.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Axis.Identity.Infrastructure.Messaging;

// WORKAROUND: see docs/WORKAROUNDS.md#team-account-hard-delete-modulith-cancellers
internal sealed class TeamAccountHardDeleteHandler(
    ITeamAccountRepository teamAccountRepo,
    ITeamAccountExecutionCanceller executionCanceller,
    ITeamAccountFormTaskCanceller formTaskCanceller,
    ITeamAccountIdentityPurger identityPurger,
    IConfiguration configuration,
    ILogger<TeamAccountHardDeleteHandler> logger)
{
    public async Task Handle(TeamAccountHardDeleteJob job, CancellationToken cancellationToken)
    {
        TeamAccount? teamAccount = await teamAccountRepo.GetByIdAsync(job.TeamAccountId, cancellationToken);
        if (teamAccount is null)
            return;

        if (teamAccount.Status != TeamAccountStatus.DeletionScheduled)
            return;

        if (teamAccount.ScheduledHardDeleteAt is DateTime scheduled && scheduled > DateTime.UtcNow)
            return;

        string? logoUrl = teamAccount.LogoUrl;

        await executionCanceller.CancelAllForTeamAccountAsync(job.TeamAccountId, cancellationToken);
        await formTaskCanceller.CancelPendingForTeamAccountAsync(job.TeamAccountId, cancellationToken);
        await DropModuleSchemasAsync(job.TeamAccountId, cancellationToken);
        await identityPurger.PurgeAsync(job.TeamAccountId, logoUrl, cancellationToken);
    }

    private async Task DropModuleSchemasAsync(Guid teamAccountId, CancellationToken cancellationToken)
    {
        (string ConnectionString, string Module)[] modules =
        [
            (configuration.GetConnectionString("Identity")
                ?? throw new InvalidOperationException("ConnectionStrings:Identity is required"),
                "Identity"),
            (configuration.GetConnectionString("DataModeling")
                ?? throw new InvalidOperationException("ConnectionStrings:DataModeling is required"),
                "DataModeling"),
            (configuration.GetConnectionString("FormBuilder")
                ?? throw new InvalidOperationException("ConnectionStrings:FormBuilder is required"),
                "FormBuilder"),
            (configuration.GetConnectionString("WorkflowBuilder")
                ?? throw new InvalidOperationException("ConnectionStrings:WorkflowBuilder is required"),
                "WorkflowBuilder"),
            (configuration.GetConnectionString("WorkflowEngine")
                ?? throw new InvalidOperationException("ConnectionStrings:WorkflowEngine is required"),
                "WorkflowEngine"),
        ];

        foreach ((string connectionString, string module) in modules)
        {
            await TenantSchemaDropper.DropAsync(
                connectionString,
                teamAccountId,
                logger,
                module,
                cancellationToken);
        }
    }
}
