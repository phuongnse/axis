using Axis.FormBuilder.Domain.Events;
using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Infrastructure.Handlers;

/// <summary>
/// Handles FormTaskSubmitted from FormBuilder: resumes the Waiting form step with
/// the submitted data merged into the execution context, then advances execution.
/// Cross-module: FormBuilder publishes FormTaskSubmitted → WorkflowEngine consumes.
/// </summary>
internal sealed class FormTaskSubmittedHandler(
    WorkflowEngineDbContext context,
    IStepDispatcher dispatcher,
    ILogger<FormTaskSubmittedHandler> logger)
{
    public async Task Handle(FormTaskSubmitted @event, CancellationToken ct)
    {
        WorkflowExecution? execution = await context.WorkflowExecutions
            .Include(e => e.Steps)
            .FirstOrDefaultAsync(e => e.Id == @event.ExecutionId, ct);

        if (execution is null)
        {
            logger.LogWarning(
                "FormTaskSubmittedHandler: execution {ExecutionId} not found", @event.ExecutionId);
            return;
        }

        ExecutionStep? step = execution.Steps.FirstOrDefault(s => s.Id == @event.ExecutionStepId);
        if (step is null)
        {
            logger.LogWarning(
                "FormTaskSubmittedHandler: step {StepId} not found in execution {ExecutionId}",
                @event.ExecutionStepId, @event.ExecutionId);
            return;
        }

        // Idempotency: step already completed (re-delivery or duplicate event)
        if (step.IsTerminal)
        {
            logger.LogInformation(
                "FormTaskSubmittedHandler: step {StepId} already terminal ({Status}), skipping",
                @event.ExecutionStepId, step.Status);
            return;
        }

        logger.LogInformation(
            "Resuming form step {StepId} in execution {ExecutionId} with {FieldCount} submitted fields",
            @event.ExecutionStepId, @event.ExecutionId, @event.SubmittedData.Count);

        execution.CompleteStep(@event.ExecutionStepId, @event.SubmittedData);
        execution.MergeContext(@event.SubmittedData);

        await context.SaveChangesAsync(ct);

        await dispatcher.PublishAsync(
            new ExecuteNextStepMessage(@event.ExecutionId, @event.OrganizationId), ct);
    }
}
