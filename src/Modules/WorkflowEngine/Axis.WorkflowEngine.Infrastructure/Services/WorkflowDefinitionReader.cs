using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.ReadModels;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.WorkflowEngine.Infrastructure.Services;

internal sealed class WorkflowDefinitionReader(WorkflowEngineDbContext context) : IWorkflowDefinitionReader
{
    public async Task<bool> IsActiveAsync(
        Guid workflowDefinitionId, Guid tenantId, CancellationToken ct = default)
        => await context.WorkflowActiveStatuses
            .AnyAsync(w => w.WorkflowId == workflowDefinitionId
                        && w.tenantId == tenantId
                        && w.IsActive, ct);

    public async Task<WorkflowSnapshot?> GetSnapshotAsync(
        Guid workflowDefinitionId, Guid tenantId, CancellationToken ct = default)
        => await context.WorkflowSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.WorkflowId == workflowDefinitionId
                                   && w.tenantId == tenantId, ct);
}
