using Axis.WorkflowEngine.Application.DTOs;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
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

    public async Task<ExecutionResponse?> GetWithStepsAsync(
        Guid executionId, Guid organizationId, CancellationToken ct = default)
    {
        WorkflowExecution? execution = await context.WorkflowExecutions
            .AsNoTracking()
            .Include(e => e.Steps)
            .FirstOrDefaultAsync(e => e.Id == executionId && e.OrganizationId == organizationId, ct);

        if (execution is null)
            return null;

        IReadOnlyList<ExecutionStepResponse> steps = execution.Steps
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new ExecutionStepResponse(
                s.Id,
                s.StepDefinitionId,
                s.Name,
                s.StepType.ToString(),
                s.DisplayOrder,
                s.Status.ToString(),
                s.InputSnapshot,
                s.OutputSnapshot,
                s.ErrorDetails,
                s.StartedAt,
                s.CompletedAt,
                s.CreatedAt))
            .ToList();

        return new ExecutionResponse(
            execution.Id,
            execution.WorkflowDefinitionId,
            execution.Status.ToString(),
            execution.TriggerType.ToString(),
            execution.TriggeredByUserId,
            execution.RetryOfExecutionId,
            execution.ErrorMessage,
            execution.Context,
            execution.CreatedAt,
            execution.StartedAt,
            execution.CompletedAt,
            steps);
    }

    public async Task<(IReadOnlyList<ExecutionSummaryResponse> Items, int TotalCount)> GetPagedAsync(
        Guid organizationId, int page, int pageSize, ExecutionStatus? status = null, CancellationToken ct = default)
    {
        IQueryable<WorkflowExecution> query = context.WorkflowExecutions
            .AsNoTracking()
            .Where(e => e.OrganizationId == organizationId);

        if (status.HasValue)
            query = query.Where(e => e.Status == status.Value);

        int total = await query.CountAsync(ct);

        List<ExecutionSummaryResponse> items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new ExecutionSummaryResponse(
                e.Id,
                e.WorkflowDefinitionId,
                e.Status.ToString(),
                e.TriggerType.ToString(),
                e.TriggeredByUserId,
                e.RetryOfExecutionId,
                e.ErrorMessage,
                e.CreatedAt,
                e.StartedAt,
                e.CompletedAt))
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(IReadOnlyList<ExecutionSummaryResponse> Items, int TotalCount)> GetPagedByWorkflowAsync(
        Guid workflowId, Guid organizationId, int page, int pageSize, ExecutionStatus? status = null, CancellationToken ct = default)
    {
        IQueryable<WorkflowExecution> query = context.WorkflowExecutions
            .AsNoTracking()
            .Where(e => e.WorkflowDefinitionId == workflowId && e.OrganizationId == organizationId);

        if (status.HasValue)
            query = query.Where(e => e.Status == status.Value);

        int total = await query.CountAsync(ct);

        List<ExecutionSummaryResponse> items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new ExecutionSummaryResponse(
                e.Id,
                e.WorkflowDefinitionId,
                e.Status.ToString(),
                e.TriggerType.ToString(),
                e.TriggeredByUserId,
                e.RetryOfExecutionId,
                e.ErrorMessage,
                e.CreatedAt,
                e.StartedAt,
                e.CompletedAt))
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<IReadOnlyList<ExecutionSummaryResponse>> GetRetriesAsync(
        Guid originalExecutionId, Guid organizationId, CancellationToken ct = default)
        => await context.WorkflowExecutions
            .AsNoTracking()
            .Where(e => e.RetryOfExecutionId == originalExecutionId && e.OrganizationId == organizationId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new ExecutionSummaryResponse(
                e.Id,
                e.WorkflowDefinitionId,
                e.Status.ToString(),
                e.TriggerType.ToString(),
                e.TriggeredByUserId,
                e.RetryOfExecutionId,
                e.ErrorMessage,
                e.CreatedAt,
                e.StartedAt,
                e.CompletedAt))
            .ToListAsync(ct);
}
