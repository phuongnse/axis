using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Infrastructure.Persistence;

namespace Axis.Testing.TestDoubles;

internal sealed class NullWorkflowBuilderUnitOfWork(WorkflowBuilderDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        context.SaveChangesAsync(ct);
}
