using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.WorkflowEngine.Infrastructure.Repositories;

internal sealed class ExecutionRepository(WorkflowEngineDbContext context) : IExecutionRepository
{
    public async Task AddAsync(WorkflowExecution execution, CancellationToken ct = default)
        => await context.WorkflowExecutions.AddAsync(execution, ct);

    public async Task<WorkflowExecution?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
        => await context.WorkflowExecutions
            .FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == organizationId, ct);

    public async Task<IReadOnlyList<WorkflowExecution>> GetAllAsync(Guid organizationId, CancellationToken ct = default)
        => await context.WorkflowExecutions
            .Where(e => e.OrganizationId == organizationId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkflowExecution>> GetByWorkflowAsync(
        Guid workflowId, Guid organizationId, CancellationToken ct = default)
        => await context.WorkflowExecutions
            .Where(e => e.WorkflowDefinitionId == workflowId && e.OrganizationId == organizationId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);
}
