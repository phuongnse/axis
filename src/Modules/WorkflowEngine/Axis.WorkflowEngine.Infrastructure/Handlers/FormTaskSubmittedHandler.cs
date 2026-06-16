using axis.formbuilder.events;
using Axis.FormBuilder.Contracts;
using Axis.Shared.Application;
using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Infrastructure.Handlers;

/// <summary>
/// Handles FormTaskSubmitted from FormBuilder: resumes the Waiting form step with
/// the submitted data merged into the execution context, then advances execution.
///
/// <para>
/// Cross-module consumer: subscribes to the Avro <see cref="FormTaskSubmittedEvent"/>
/// published by FormBuilder over Kafka (ADR-019). Previously consumed the
/// in-process domain event directly — that pattern violated ADR-010 and was
/// tracked as a workaround until this PR.
/// </para>
/// </summary>
internal sealed class FormTaskSubmittedHandler(
    IExecutionRepository execRepo,
    IUnitOfWork uow,
    IStepDispatcher dispatcher,
    ILogger<FormTaskSubmittedHandler> logger)
{
    public async Task Handle(FormTaskSubmittedEvent @event, CancellationToken ct)
    {
        Guid executionId = @event.ExecutionId();
        Guid executionStepId = @event.ExecutionStepId();
        Guid teamAccountId = @event.TeamAccountId();
        IReadOnlyDictionary<string, object?> submittedData = @event.SubmittedData();

        WorkflowExecution? execution = await execRepo.GetByIdWithStepsAsync(
            executionId, teamAccountId, ct);

        if (execution is null)
        {
            logger.LogWarning(
                "FormTaskSubmittedHandler: execution {ExecutionId} not found", executionId);
            return;
        }

        ExecutionStep? step = execution.Steps.FirstOrDefault(s => s.Id == executionStepId);
        if (step is null)
        {
            logger.LogWarning(
                "FormTaskSubmittedHandler: step {StepId} not found in execution {ExecutionId}",
                executionStepId, executionId);
            return;
        }

        // Idempotency: step already completed (re-delivery or duplicate event)
        if (step.IsTerminal)
        {
            logger.LogInformation(
                "FormTaskSubmittedHandler: step {StepId} already terminal ({Status}), skipping",
                executionStepId, step.Status);
            return;
        }

        logger.LogInformation(
            "Resuming form step {StepId} in execution {ExecutionId} with {FieldCount} submitted fields",
            executionStepId, executionId, submittedData.Count);

        execution.CompleteStep(executionStepId, submittedData);
        execution.MergeContext(submittedData);

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            // Another Wolverine worker already committed this form step completion.
            logger.LogInformation(
                "FormTaskSubmittedHandler: concurrent completion detected for step {StepId} — skipping",
                executionStepId);
            return;
        }

        await dispatcher.PublishAsync(
            new ExecuteNextStepMessage(executionId, teamAccountId), ct);
    }
}
