using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.WorkflowEngine.Infrastructure.Services;

internal sealed class WorkflowDefinitionReader(WorkflowEngineDbContext context) : IWorkflowDefinitionReader
{
    public async Task<bool> IsActiveAsync(
        Guid workflowDefinitionId, Guid organizationId, CancellationToken ct = default)
    {
        var count = await context.Database
            .SqlQueryRaw<int>(
                "SELECT CAST(COUNT(*) AS int) AS \"Value\" FROM workflow_definitions " +
                "WHERE id = {0} AND organization_id = {1} AND status = 'Active'",
                workflowDefinitionId, organizationId)
            .FirstAsync(ct);
        return count > 0;
    }
}
