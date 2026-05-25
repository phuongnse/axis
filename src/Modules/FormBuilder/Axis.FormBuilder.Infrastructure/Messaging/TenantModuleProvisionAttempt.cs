using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Identity.Contracts;
using Axis.Shared.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.FormBuilder.Infrastructure.Messaging;

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
        string connectionString = configuration.GetConnectionString("FormBuilder")
            ?? throw new InvalidOperationException("Missing connection string 'FormBuilder'.");

        try
        {
            await TenantSchemaProvisioner.ProvisionAsync(
                connectionString,
                $"axis.tenant.formbuilder:{organizationId:N}",
                organizationId,
                ct => TenantSchemaProvisioner.MigrateWithFixedTenantAsync(
                    connectionString,
                    organizationId,
                    (DbContextOptions<FormBuilderDbContext> options) =>
                        new FormBuilderDbContext(options, new FixedTenantContext(organizationId)),
                    ct),
                logger,
                TenantModuleNames.FormBuilder,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                TenantModuleProvisionReportEventFactory.Create(
                    organizationId, TenantModuleNames.FormBuilder, succeeded: true, attempt));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "FormBuilder tenant provisioning failed on attempt {Attempt}",
                attempt);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                TenantModuleProvisionReportEventFactory.Create(
                    organizationId,
                    TenantModuleNames.FormBuilder,
                    succeeded: false,
                    attempt,
                    ex.Message));
        }
    }
}
