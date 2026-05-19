using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Infrastructure.Persistence;

namespace Axis.Api.Tests.Helpers;

internal sealed class NullWorkflowBuilderUnitOfWork(WorkflowBuilderDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        context.SaveChangesAsync(ct);
}
