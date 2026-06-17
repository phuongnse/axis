using Axis.Identity.Contracts;
using Axis.Shared.Infrastructure.Workspaces;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.WorkflowBuilder.Infrastructure.Messaging;

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
        string connectionString = configuration.GetConnectionString("WorkflowBuilder")
            ?? throw new InvalidOperationException("Missing connection string 'WorkflowBuilder'.");

        try
        {
            await WorkspaceSchemaProvisioner.ProvisionAsync(
                connectionString,
                $"axis.workspace.workflowbuilder:{workspaceId:N}",
                workspaceId,
                ct => WorkspaceSchemaProvisioner.MigrateWithFixedWorkspaceAsync(
                    connectionString,
                    workspaceId,
                    (DbContextOptions<WorkflowBuilderDbContext> options) =>
                        new WorkflowBuilderDbContext(options, new FixedWorkspaceContext(workspaceId)),
                    ct),
                logger,
                WorkspaceModuleNames.WorkflowBuilder,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                WorkspaceModuleProvisionReportEventFactory.Create(
                    workspaceId, WorkspaceModuleNames.WorkflowBuilder, succeeded: true, attempt));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "WorkflowBuilder workspace provisioning failed on attempt {Attempt}",
                attempt);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                WorkspaceModuleProvisionReportEventFactory.Create(
                    workspaceId,
                    WorkspaceModuleNames.WorkflowBuilder,
                    succeeded: false,
                    attempt,
                    ex.Message));
        }
    }
}
