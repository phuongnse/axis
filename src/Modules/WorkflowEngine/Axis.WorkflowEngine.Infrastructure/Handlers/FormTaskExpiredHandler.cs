using axis.formbuilder.events;
using Axis.FormBuilder.Contracts;
using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Infrastructure.Handlers;

/// <summary>
/// Fails the waiting form step and execution when a form task expires.
///
/// <para>
/// Cross-module consumer: subscribes to the Avro <see cref="FormTaskExpiredEvent"/>
/// published by FormBuilder over Kafka (ADR-019). Previously consumed the
/// in-process domain event directly — that pattern violated ADR-010 and was
/// tracked as a workaround until this PR.
/// </para>
/// </summary>
internal sealed class FormTaskExpiredHandler(
    IExecutionRepository execRepo,
    IStepDispatcher dispatcher,
    ILogger<FormTaskExpiredHandler> logger)
{
    public async Task Handle(FormTaskExpiredEvent @event, CancellationToken ct)
    {
        Guid executionId = @event.ExecutionId();
        Guid executionStepId = @event.ExecutionStepId();
        Guid tenantId = @event.tenantId();

        WorkflowExecution? execution = await execRepo.GetByIdWithStepsAsync(
            executionId, tenantId, ct);

        if (execution is null)
        {
            logger.LogWarning(
                "FormTaskExpiredHandler: execution {ExecutionId} not found", executionId);
            return;
        }

        ExecutionStep? step = execution.Steps.FirstOrDefault(s => s.Id == executionStepId);
        if (step is null)
        {
            logger.LogWarning(
                "FormTaskExpiredHandler: step {StepId} not found in execution {ExecutionId}",
                executionStepId,
                executionId);
            return;
        }

        if (step.IsTerminal)
        {
            logger.LogInformation(
                "FormTaskExpiredHandler: step {StepId} already terminal ({Status}), skipping",
                executionStepId,
                step.Status);
            return;
        }

        const string errorDetails =
            "The form step timed out before a submission was received.";

        logger.LogWarning(
            "Form step {StepId} timed out in execution {ExecutionId}",
            executionStepId,
            executionId);

        await dispatcher.PublishAsync(
            new StepFailedMessage(
                executionId,
                executionStepId,
                tenantId,
                errorDetails),
            ct);
    }
}
