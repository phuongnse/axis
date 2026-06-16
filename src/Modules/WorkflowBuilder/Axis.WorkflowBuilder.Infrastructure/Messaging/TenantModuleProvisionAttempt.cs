using Axis.Identity.Contracts;
using Axis.Shared.Infrastructure.Tenancy;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.WorkflowBuilder.Infrastructure.Messaging;

internal static class TenantModuleProvisionAttempt
{
    public static async Task RunAsync(
        Guid teamAccountId,
        int attempt,
        IConfiguration configuration,
        IMessageBus messageBus,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetConnectionString("WorkflowBuilder")
            ?? throw new InvalidOperationException("Missing connection string 'WorkflowBuilder'.");

        try
        {
            await TenantSchemaProvisioner.ProvisionAsync(
                connectionString,
                $"axis.tenant.workflowbuilder:{teamAccountId:N}",
                teamAccountId,
                ct => TenantSchemaProvisioner.MigrateWithFixedTenantAsync(
                    connectionString,
                    teamAccountId,
                    (DbContextOptions<WorkflowBuilderDbContext> options) =>
                        new WorkflowBuilderDbContext(options, new FixedTenantContext(teamAccountId)),
                    ct),
                logger,
                TenantModuleNames.WorkflowBuilder,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                TenantModuleProvisionReportEventFactory.Create(
                    teamAccountId, TenantModuleNames.WorkflowBuilder, succeeded: true, attempt));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "WorkflowBuilder tenant provisioning failed on attempt {Attempt}",
                attempt);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                TenantModuleProvisionReportEventFactory.Create(
                    teamAccountId,
                    TenantModuleNames.WorkflowBuilder,
                    succeeded: false,
                    attempt,
                    ex.Message));
        }
    }
}
