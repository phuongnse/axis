using axis.identity.events;
using Axis.DataModeling.Infrastructure.Persistence;
using Axis.Identity.Contracts;
using Axis.Shared.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.DataModeling.Infrastructure.Messaging;

internal static class WorkspaceModuleProvisionAttempt
{
    public static async Task RunAsync(
        Guid workspaceId,
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
            await WorkspaceSchemaProvisioner.ProvisionAsync(
                connectionString,
                $"axis.workspace.datamodeling:{workspaceId:N}",
                workspaceId,
                ct => WorkspaceSchemaProvisioner.MigrateWithFixedWorkspaceAsync(
                    connectionString,
                    workspaceId,
                    (DbContextOptions<DataModelingDbContext> options) =>
                        new DataModelingDbContext(options, new FixedWorkspaceContext(workspaceId)),
                    ct),
                logger,
                WorkspaceModuleNames.DataModeling,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                WorkspaceModuleProvisionReportEventFactory.Create(
                    workspaceId, WorkspaceModuleNames.DataModeling, succeeded: true, attempt));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "DataModeling workspace provisioning failed on attempt {Attempt}",
                attempt);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                WorkspaceModuleProvisionReportEventFactory.Create(
                    workspaceId,
                    WorkspaceModuleNames.DataModeling,
                    succeeded: false,
                    attempt,
                    ex.Message));
        }
    }
}
