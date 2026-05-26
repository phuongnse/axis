using Axis.Shared.Application.Organizations;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;

namespace Axis.WorkflowEngine.Infrastructure.Organizations;

internal sealed class OrganizationExecutionCanceller(
    IExecutionRepository execRepo,
    IUnitOfWork uow) : IOrganizationExecutionCanceller
{
    public async Task CancelAllForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkflowExecution> executions =
            await execRepo.GetCancellableByOrganizationAsync(organizationId, cancellationToken);

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

        await uow.SaveChangesAsync(cancellationToken);
    }
}
