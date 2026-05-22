using Axis.FormBuilder.Domain.Events;
using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Infrastructure.Handlers;

/// <summary>
/// US-089: Fails the waiting form step and execution when a form task expires.
/// Cross-module: FormBuilder publishes FormTaskExpired → WorkflowEngine consumes.
/// </summary>
internal sealed class FormTaskExpiredHandler(
    IExecutionRepository execRepo,
    IStepDispatcher dispatcher,
    ILogger<FormTaskExpiredHandler> logger)
{
    public async Task Handle(FormTaskExpired @event, CancellationToken ct)
    {
        WorkflowExecution? execution = await execRepo.GetByIdWithStepsAsync(
            @event.ExecutionId, @event.OrganizationId, ct);

        if (execution is null)
        {
            logger.LogWarning(
                "FormTaskExpiredHandler: execution {ExecutionId} not found", @event.ExecutionId);
            return;
        }

        ExecutionStep? step = execution.Steps.FirstOrDefault(s => s.Id == @event.ExecutionStepId);
        if (step is null)
        {
            logger.LogWarning(
                "FormTaskExpiredHandler: step {StepId} not found in execution {ExecutionId}",
                @event.ExecutionStepId,
                @event.ExecutionId);
            return;
        }

        if (step.IsTerminal)
        {
            logger.LogInformation(
                "FormTaskExpiredHandler: step {StepId} already terminal ({Status}), skipping",
                @event.ExecutionStepId,
                step.Status);
            return;
        }

        const string errorDetails =
            "The form step timed out before a submission was received.";

        logger.LogWarning(
            "Form step {StepId} timed out in execution {ExecutionId}",
            @event.ExecutionStepId,
            @event.ExecutionId);

        await dispatcher.PublishAsync(
            new StepFailedMessage(
                @event.ExecutionId,
                @event.ExecutionStepId,
                @event.OrganizationId,
                errorDetails),
            ct);
    }
}
