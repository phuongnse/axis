using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Identity.Contracts;
using Axis.Shared.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.FormBuilder.Infrastructure.Messaging;

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
        string connectionString = configuration.GetConnectionString("FormBuilder")
            ?? throw new InvalidOperationException("Missing connection string 'FormBuilder'.");

        try
        {
            await WorkspaceSchemaProvisioner.ProvisionAsync(
                connectionString,
                $"axis.workspace.formbuilder:{workspaceId:N}",
                workspaceId,
                ct => WorkspaceSchemaProvisioner.MigrateWithFixedWorkspaceAsync(
                    connectionString,
                    workspaceId,
                    (DbContextOptions<FormBuilderDbContext> options) =>
                        new FormBuilderDbContext(options, new FixedWorkspaceContext(workspaceId)),
                    ct),
                logger,
                WorkspaceModuleNames.FormBuilder,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                WorkspaceModuleProvisionReportEventFactory.Create(
                    workspaceId, WorkspaceModuleNames.FormBuilder, succeeded: true, attempt));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "FormBuilder workspace provisioning failed on attempt {Attempt}",
                attempt);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                WorkspaceModuleProvisionReportEventFactory.Create(
                    workspaceId,
                    WorkspaceModuleNames.FormBuilder,
                    succeeded: false,
                    attempt,
                    ex.Message));
        }
    }
}
