using Axis.Shared.Application.Workspaces;
using Axis.Shared.Infrastructure.Workspaces;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Axis.WorkflowEngine.Infrastructure.Workspaces;

internal sealed class WorkspaceExecutionCanceller(IConfiguration configuration) : IWorkspaceExecutionCanceller
{
    public async Task CancelAllForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetConnectionString("WorkflowEngine")
            ?? throw new InvalidOperationException("ConnectionStrings:WorkflowEngine is required.");

        DbContextOptionsBuilder<WorkflowEngineDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);

        await using WorkflowEngineDbContext context = new(
            optionsBuilder.Options,
            new FixedWorkspaceContext(workspaceId));

        List<WorkflowExecution> executions = await context.WorkflowExecutions
            .Where(e => e.workspaceId == workspaceId
                        && (e.Status == ExecutionStatus.Pending || e.Status == ExecutionStatus.Running))
            .ToListAsync(cancellationToken);

        foreach (WorkflowExecution execution in executions)
        {
            try
            {
                execution.Cancel();
            }
            catch (InvalidOperationException)
            {
                // Skip executions that cannot be cancelled.
            }
        }

        if (executions.Count > 0)
            await context.SaveChangesAsync(cancellationToken);
    }
}
