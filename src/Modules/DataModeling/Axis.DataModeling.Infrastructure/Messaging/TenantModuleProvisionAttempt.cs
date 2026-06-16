using axis.identity.events;
using Axis.DataModeling.Infrastructure.Persistence;
using Axis.Identity.Contracts;
using Axis.Shared.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.DataModeling.Infrastructure.Messaging;

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
        string connectionString = configuration.GetConnectionString("DataModeling")
            ?? throw new InvalidOperationException("Missing connection string 'DataModeling'.");

        try
        {
            await TenantSchemaProvisioner.ProvisionAsync(
                connectionString,
                $"axis.tenant.datamodeling:{teamAccountId:N}",
                teamAccountId,
                ct => TenantSchemaProvisioner.MigrateWithFixedTenantAsync(
                    connectionString,
                    teamAccountId,
                    (DbContextOptions<DataModelingDbContext> options) =>
                        new DataModelingDbContext(options, new FixedTenantContext(teamAccountId)),
                    ct),
                logger,
                TenantModuleNames.DataModeling,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                TenantModuleProvisionReportEventFactory.Create(
                    teamAccountId, TenantModuleNames.DataModeling, succeeded: true, attempt));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "DataModeling tenant provisioning failed on attempt {Attempt}",
                attempt);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                TenantModuleProvisionReportEventFactory.Create(
                    teamAccountId,
                    TenantModuleNames.DataModeling,
                    succeeded: false,
                    attempt,
                    ex.Message));
        }
    }
}
