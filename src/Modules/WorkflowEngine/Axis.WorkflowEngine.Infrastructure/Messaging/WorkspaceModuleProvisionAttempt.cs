using Axis.Identity.Contracts;
using Axis.Shared.Infrastructure.Workspaces;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.WorkflowEngine.Infrastructure.Messaging;

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
        string connectionString = configuration.GetConnectionString("WorkflowEngine")
            ?? throw new InvalidOperationException("Missing connection string 'WorkflowEngine'.");

        try
        {
            await WorkspaceSchemaProvisioner.ProvisionAsync(
                connectionString,
                $"axis.workspace.workflowengine:{workspaceId:N}",
                workspaceId,
                ct => WorkspaceSchemaProvisioner.MigrateWithFixedWorkspaceAsync(
                    connectionString,
                    workspaceId,
                    (DbContextOptions<WorkflowEngineDbContext> options) =>
                        new WorkflowEngineDbContext(options, new FixedWorkspaceContext(workspaceId)),
                    ct),
                logger,
                WorkspaceModuleNames.WorkflowEngine,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                WorkspaceModuleProvisionReportEventFactory.Create(
                    workspaceId, WorkspaceModuleNames.WorkflowEngine, succeeded: true, attempt));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "WorkflowEngine workspace provisioning failed on attempt {Attempt}",
                attempt);

            cancellationToken.ThrowIfCancellationRequested();
            await messageBus.PublishAsync(
                WorkspaceModuleProvisionReportEventFactory.Create(
                    workspaceId,
                    WorkspaceModuleNames.WorkflowEngine,
                    succeeded: false,
                    attempt,
                    ex.Message));
        }
    }
}
