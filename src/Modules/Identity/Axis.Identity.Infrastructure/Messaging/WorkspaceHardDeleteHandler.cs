using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.Workspaces;
using Axis.Shared.Infrastructure.Workspaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Axis.Identity.Infrastructure.Messaging;

// WORKAROUND: see docs/WORKAROUNDS.md#Workspace-hard-delete-modulith-cancellers
internal sealed class WorkspaceHardDeleteHandler(
    IWorkspaceRepository workspaceRepo,
    IWorkspaceExecutionCanceller executionCanceller,
    IWorkspaceFormTaskCanceller formTaskCanceller,
    IWorkspaceIdentityPurger identityPurger,
    IConfiguration configuration,
    ILogger<WorkspaceHardDeleteHandler> logger)
{
    public async Task Handle(WorkspaceHardDeleteJob job, CancellationToken cancellationToken)
    {
        Workspace? Workspace = await workspaceRepo.GetByIdAsync(job.workspaceId, cancellationToken);
        if (Workspace is null)
            return;

        if (Workspace.Status != WorkspaceStatus.DeletionScheduled)
            return;

        if (Workspace.ScheduledHardDeleteAt is DateTime scheduled && scheduled > DateTime.UtcNow)
            return;

        string? logoUrl = Workspace.LogoUrl;

        await executionCanceller.CancelAllForWorkspaceAsync(job.workspaceId, cancellationToken);
        await formTaskCanceller.CancelPendingForWorkspaceAsync(job.workspaceId, cancellationToken);
        await DropModuleSchemasAsync(job.workspaceId, cancellationToken);
        await identityPurger.PurgeAsync(job.workspaceId, logoUrl, cancellationToken);
    }

    private async Task DropModuleSchemasAsync(Guid workspaceId, CancellationToken cancellationToken)
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
            await WorkspaceSchemaDropper.DropAsync(
                connectionString,
                workspaceId,
                logger,
                module,
                cancellationToken);
        }
    }
}
