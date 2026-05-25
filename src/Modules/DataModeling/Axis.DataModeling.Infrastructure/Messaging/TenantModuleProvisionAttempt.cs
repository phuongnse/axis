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
        Guid organizationId,
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
                $"axis.tenant.datamodeling:{organizationId:N}",
                organizationId,
                ct => TenantSchemaProvisioner.MigrateWithFixedTenantAsync(
                    connectionString,
                    organizationId,
                    (DbContextOptions<DataModelingDbContext> options) =>
                        new DataModelingDbContext(options, new FixedTenantContext(organizationId)),
                    ct),
                logger,
                TenantModuleNames.DataModeling,
                cancellationToken);

            await messageBus.PublishAsync(
                TenantModuleProvisionReportEventFactory.Create(
                    organizationId, TenantModuleNames.DataModeling, succeeded: true, attempt));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "DataModeling tenant provisioning failed for organization {OrganizationId} attempt {Attempt}",
                organizationId,
                attempt);

            await messageBus.PublishAsync(
                TenantModuleProvisionReportEventFactory.Create(
                    organizationId,
                    TenantModuleNames.DataModeling,
                    succeeded: false,
                    attempt,
                    ex.Message));
        }
    }
}
