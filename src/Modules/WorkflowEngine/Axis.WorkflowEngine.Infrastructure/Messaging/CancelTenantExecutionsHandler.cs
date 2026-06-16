using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Contracts;
using Axis.WorkflowEngine.Domain.Aggregates;

namespace Axis.WorkflowEngine.Infrastructure.Messaging;

internal sealed class CancelTenantExecutionsHandler(
    IExecutionRepository execRepo,
    IUnitOfWork uow)
{
    public async Task Handle(CancelTenantExecutionsCommand command, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkflowExecution> executions =
            await execRepo.GetCancellableByTenantAsync(command.tenantId, cancellationToken);

        foreach (WorkflowExecution execution in executions)
        {
            try
            {
                execution.Cancel();
            }
            catch (InvalidOperationException)
            {
                // Already terminal or not cancellable — skip.
            }
        }

        await uow.SaveChangesAsync(cancellationToken);
    }
}
