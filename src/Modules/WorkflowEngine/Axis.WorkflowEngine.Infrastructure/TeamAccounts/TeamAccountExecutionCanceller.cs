using Axis.Shared.Application.TeamAccounts;
using Axis.Shared.Infrastructure.Tenancy;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Axis.WorkflowEngine.Infrastructure.TeamAccounts;

internal sealed class TeamAccountExecutionCanceller(IConfiguration configuration) : ITeamAccountExecutionCanceller
{
    public async Task CancelAllForTeamAccountAsync(Guid teamAccountId, CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetConnectionString("WorkflowEngine")
            ?? throw new InvalidOperationException("ConnectionStrings:WorkflowEngine is required.");

        DbContextOptionsBuilder<WorkflowEngineDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);

        await using WorkflowEngineDbContext context = new(
            optionsBuilder.Options,
            new FixedTenantContext(teamAccountId));

        List<WorkflowExecution> executions = await context.WorkflowExecutions
            .Where(e => e.TeamAccountId == teamAccountId
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
