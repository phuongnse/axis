using Axis.Identity.Contracts;
using Axis.Shared.Infrastructure.Tenancy;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.WorkflowEngine.Infrastructure.Messaging;

internal static class TenantModuleProvisionAttempt
{
    public static async Task RunAsync(
        Guid organizationId,
        int attempt,
        IConfiguration configuration,
        IMessageBus messageBus,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetConnectionString("WorkflowEngine")
            ?? throw new InvalidOperationException("Missing connection string 'WorkflowEngine'.");

        try
        {
            await TenantSchemaProvisioner.ProvisionAsync(
                connectionString,
                $"axis.tenant.workflowengine:{organizationId:N}",
                organizationId,
                ct => TenantSchemaProvisioner.MigrateWithFixedTenantAsync(
                    connectionString,
                    organizationId,
                    (DbContextOptions<WorkflowEngineDbContext> options) =>
                        new WorkflowEngineDbContext(options, new FixedTenantContext(organizationId)),
                    ct),
                logger,
                TenantModuleNames.WorkflowEngine,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                TenantModuleProvisionReportEventFactory.Create(
                    organizationId, TenantModuleNames.WorkflowEngine, succeeded: true, attempt));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "WorkflowEngine tenant provisioning failed on attempt {Attempt}",
                attempt);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                TenantModuleProvisionReportEventFactory.Create(
                    organizationId,
                    TenantModuleNames.WorkflowEngine,
                    succeeded: false,
                    attempt,
                    ex.Message));
        }
    }
}
